using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexTracker.Core;
using Forms = System.Windows.Forms;
using WpfCursors = System.Windows.Input.Cursors;

namespace CodexTracker;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly MainViewModel _viewModel = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly LocalUsageAnalyticsService _analytics = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly bool _demo;
    private AppSettings _settings;
    private CodexAppServerClient? _client;
    private Forms.NotifyIcon? _tray;
    private int _connecting;
    private int _analyticsRunning;
    private int _refreshTick;
    private bool _adjustingCompactSize;
    private bool _dragCandidate;
    private bool _manualResize;
    private bool _resizeGestureActive;
    private bool _suppressDoubleClickToggle;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _resizeStartScreen;
    private Rect _resizeStartBounds;
    private ResizeWorkArea _resizeWorkArea;
    private ResizeEdge _resizeEdge;
    // The compact XAML reserves a 52 x 42 gauge surface inside the 62 x 52 window.
    private const double CompactAspectRatio = 62d / 52d;
    private const double CompactMinWidth = 62d;
    private const double CompactMaxWidth = 320d;
    private const double ResizeBorderThickness = 6d;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    [Flags]
    private enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }

    public MainWindow(bool demo = false)
    {
        InitializeComponent();
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(OnWindowPreviewMouseDown), true);
        AddHandler(Mouse.PreviewMouseMoveEvent, new System.Windows.Input.MouseEventHandler(OnWindowPreviewMouseMove), true);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(OnWindowPreviewMouseUp), true);
        LostMouseCapture += (_, _) => { if (_manualResize) FinishManualResize(false); };
        Deactivated += (_, _) => { if (_manualResize) FinishManualResize(false); };
        Chrome.IsHitTestVisible = false;
        DataContext = _viewModel;
        _demo = demo;
        _settings = _store.Load();
        Width = Math.Clamp(IsLegacyDefaultCompactSize(_settings) ? CompactMinWidth : _settings.Width, CompactMinWidth, CompactMaxWidth);
        Height = Math.Clamp(_settings.Height, 50, 620);
        Left = Math.Max(SystemParameters.WorkArea.Left, Math.Min(_settings.Left, SystemParameters.WorkArea.Right - Width));
        Top = Math.Max(SystemParameters.WorkArea.Top, Math.Min(_settings.Top, SystemParameters.WorkArea.Bottom - Height));
        Topmost = _settings.IsTopmost;
        ThemeManager.Apply(_settings.Theme);
        _viewModel.Topmost = Topmost;
        _viewModel.SetCurrency(_settings.CurrencyCode);
        _viewModel.Expanded = _settings.IsExpanded;
        ApplyWindowModeSize();
        SizeChanged += OnWindowSizeChanged;
        Loaded += (_, _) =>
        {
            _refreshTimer.Start();
            _ = LoadAsync();
            if (_viewModel.Expanded) _ = RefreshAnalyticsAsync();
        };
        Closing += OnClosing;
        SourceInitialized += OnSourceInitialized;
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        CreateTray();
    }

    private async Task LoadAsync()
    {
        if (_demo)
        {
            _viewModel.Apply(new RateLimitSnapshot([new("codex:primary", "Weekly limit", 16, DateTimeOffset.Now.AddDays(3), 10080)], "demo", "Credits: unlimited", "Reset credits: 2", DateTimeOffset.UtcNow), new(12400, 240300, 11.4m, 16, 100, [new("gpt-5.6-terra", 240300, 11.4m, true)]), _settings.CurrencyCode);
            return;
        }
        if (Interlocked.Exchange(ref _connecting, 1) == 1) return;
        try
        {
            var executable = CodexExecutableDiscovery.Find(_settings.CodexPath);
            if (executable is null) { _viewModel.Status = "Erro: Codex CLI não encontrado"; SanitizedLogger.Write("Discovery failed"); return; }
            SanitizedLogger.Write("App-server starting: " + executable);
            if (_client is not null) await _client.DisposeAsync();
            _client = new CodexAppServerClient(executable);
            _client.StatusChanged += status => Dispatcher.Invoke(() => _viewModel.Status = status.StartsWith("Connection interrupted") ? "Stale: reconectando" : status);
            _client.SnapshotUpdated += snapshot =>
            {
                _ = Dispatcher.InvokeAsync(() => _viewModel.ApplyQuota(snapshot));
                var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
                SanitizedLogger.Write($"Quota snapshot summary: windows={snapshot.Windows.Count}, weekly={(weekly is null ? "missing" : weekly.UsedPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))}");
            };
            await _client.StartAsync(_shutdown.Token);
            SanitizedLogger.Write("App-server initialized and read");
        }
        catch (Exception exception) { _viewModel.Status = "Erro de conexão"; SanitizedLogger.Write("Connect error: " + exception.GetType().Name); }
        finally { Volatile.Write(ref _connecting, 0); }
    }

    private async Task RefreshAsync()
    {
        if (_client is null) { await LoadAsync(); return; }
        try
        {
            await _client.RefreshAsync(_shutdown.Token);
            if (_viewModel.Expanded && Interlocked.Increment(ref _refreshTick) % 5 == 0) _ = RefreshAnalyticsAsync();
        }
        catch (Exception exception) { _viewModel.Status = "Stale: tentando reconectar"; SanitizedLogger.Write("Refresh error: " + exception.GetType().Name); await LoadAsync(); }
    }

    private async Task RefreshAnalyticsAsync()
    {
        if (Interlocked.Exchange(ref _analyticsRunning, 1) == 1) return;
        try
        {
            var usage = await _analytics.ReadAsync(_settings.UsdBrl);
            if (_viewModel.Expanded && _client?.Snapshot is { } snapshot)
                await Dispatcher.InvokeAsync(() => _viewModel.Apply(snapshot, usage, _settings.CurrencyCode));
        }
        catch (Exception exception)
        {
            SanitizedLogger.Write("Analytics refresh error: " + exception.GetType().Name);
        }
        finally { Volatile.Write(ref _analyticsRunning, 0); }
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon { Visible = true, Text = "Codex Tracker", Icon = CreateTrayIcon() };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => { Show(); Activate(); });
        menu.Items.Add("Alternar modo detalhado", null, (_, _) => Dispatcher.Invoke(() => ToggleDetailed(this, new RoutedEventArgs())));
        menu.Items.Add("Atualizar", null, async (_, _) => await RefreshAsync());
        menu.Items.Add("Sair", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        if (Environment.ProcessPath is { } executable && System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } applicationIcon)
        {
            using (applicationIcon) return (System.Drawing.Icon)applicationIcon.Clone();
        }

        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.FromArgb(72, 191, 168))) graphics.FillEllipse(brush, 4, 4, 24, 24);
        var handle = bitmap.GetHicon();
        try { return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private void ToggleTopmost(object sender, RoutedEventArgs e) { Topmost = !Topmost; _viewModel.Topmost = Topmost; Save(); }
    private void ToggleDetailed(object sender, RoutedEventArgs e)
    {
        _viewModel.Expanded = !_viewModel.Expanded;
        ApplyWindowModeSize();
        Save();
        if (_viewModel.Expanded) _ = RefreshAnalyticsAsync();
    }
    private void Settings(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            ThemeManager.Apply(_settings.Theme);
            ApplyBackdrop(_settings.Theme);
            ApplyWindowModeSize();

            return;
        }

        DetailedBox.IsChecked = _viewModel.Expanded;
        TopmostBox.IsChecked = Topmost;
        ThemeToggle.IsChecked = _settings.Theme == "Escuro";
        CurrencyBox.SelectedIndex = SettingsStore.NormalizeCurrency(_settings.CurrencyCode) == "USD" ? 1 : 0;
        UpdateCurrencyRateVisibility();
        RateBox.Text = _settings.UsdBrl.ToString(System.Globalization.CultureInfo.InvariantCulture);
        PathBox.Text = _settings.CodexPath ?? "";
        SettingsPanel.Visibility = Visibility.Visible;
        MinWidth = MaxWidth = 300;
        Width = 300;
        MinHeight = 440;
        MaxHeight = 720;
        Height = Math.Max(Height, 440);
    }
    private void CurrencyChanged(object sender, SelectionChangedEventArgs e) => UpdateCurrencyRateVisibility();
    private void UpdateCurrencyRateVisibility() => RatePanel.Visibility = CurrencyBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
    private void Browse(object sender, RoutedEventArgs e) { var dialog = new Microsoft.Win32.OpenFileDialog(); if (dialog.ShowDialog() == true) PathBox.Text = dialog.FileName; }
    private void AutoDetect(object sender, RoutedEventArgs e) => PathBox.Text = CodexExecutableDiscovery.Find(null) ?? "";
    private void OpenLog(object sender, RoutedEventArgs e) { Directory.CreateDirectory(Path.GetDirectoryName(SanitizedLogger.LogPath)!); Process.Start(new ProcessStartInfo("notepad.exe", SanitizedLogger.LogPath) { UseShellExecute = true }); }
    private async void TestPath(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(PathBox.Text)) throw new FileNotFoundException();
            await using var probe = new CodexAppServerClient(PathBox.Text);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await probe.StartAsync(timeout.Token);
            var weekly = probe.Snapshot?.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
            _viewModel.Status = weekly is null
                ? "Conectado - quota semanal indisponível"
                : $"Conectado - quota semanal restante {QuotaPresentation.FormatWeeklyRemaining(weekly)}";
        }
        catch (Exception exception) { _viewModel.Status = "Teste falhou"; SanitizedLogger.Write("Path test error: " + exception.GetType().Name); }
    }
    private void ApplySettings(object sender, RoutedEventArgs e)
    {
        decimal.TryParse(RateBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate);
        var theme = ThemeToggle.IsChecked == true ? "Escuro" : "Claro";
        var currency = SettingsStore.NormalizeCurrency((CurrencyBox.SelectedItem as ComboBoxItem)?.Tag as string);
        _settings = _settings with { CodexPath = string.IsNullOrWhiteSpace(PathBox.Text) ? null : PathBox.Text, UsdBrl = rate > 0 ? rate : 5.5m, Theme = theme, CurrencyCode = currency };
        ThemeManager.Apply(theme);
        ApplyBackdrop(theme);
        _viewModel.SetCurrency(currency);
        _viewModel.Expanded = DetailedBox.IsChecked == true;
        Topmost = TopmostBox.IsChecked == true;
        ApplyWindowModeSize();
        Save(); SettingsPanel.Visibility = Visibility.Collapsed;
        if (_viewModel.Expanded) _ = RefreshAnalyticsAsync();
        _ = LoadAsync();
    }
    private void PreviewTheme(object sender, RoutedEventArgs e)
    {
        var theme = ThemeToggle.IsChecked == true ? "Escuro" : "Claro";
        ThemeManager.Apply(theme);
        ApplyBackdrop(theme);
    }
    private void ApplyBackdrop(string theme) => Backdrop.Apply(this, string.Equals(theme, "Escuro", StringComparison.OrdinalIgnoreCase));
    private void CloseWindow(object sender, RoutedEventArgs e) => Hide();

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyBackdrop(_settings.Theme);
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        _dragCandidate = false;
        _resizeGestureActive = false; // A new down always begins a distinct gesture.
        var position = e.GetPosition(this);
        _resizeEdge = GetResizeEdge(position);
        if (_resizeEdge != ResizeEdge.None)
        {
            _resizeGestureActive = true;
            _resizeStartScreen = GetScreenPoint(position);
            _resizeStartBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
            _resizeWorkArea = GetResizeWorkArea();
            _manualResize = CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ClickCount > 1)
        {
            // A double-click on read-only content is a mode shortcut. Keep settings and
            // controls out of it, and ignore the third click of a longer click sequence.
            if (e.ClickCount == 2 &&
                !_suppressDoubleClickToggle &&
                SettingsPanel.Visibility != Visibility.Visible &&
                e.OriginalSource is DependencyObject source &&
                !IsInteractive(source))
            {
                ToggleDetailed(this, new RoutedEventArgs());
                e.Handled = true;
            }
            return;
        }

        _dragStart = position;
        _suppressDoubleClickToggle = SettingsPanel.Visibility == Visibility.Visible ||
                                     e.OriginalSource is DependencyObject dragSource && IsInteractive(dragSource);
        _dragCandidate = true;
    }

    private void OnWindowPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_manualResize)
        {
            if (e.LeftButton != MouseButtonState.Pressed) FinishManualResize(true);
            else ApplyManualResize(GetScreenPoint(position));
            e.Handled = true;
            return;
        }

        if (_resizeGestureActive)
        {
            if (e.LeftButton != MouseButtonState.Pressed) _resizeGestureActive = false;
            e.Handled = true;
            return;
        }

        Cursor = CursorFor(GetResizeEdge(position));
        if (!_dragCandidate) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = false;
            return;
        }

        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragCandidate = false;
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero) return;

        ReleaseCapture();
        SendMessage(windowHandle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        e.Handled = true;
    }

    private void OnWindowPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (_manualResize) FinishManualResize(true);
        _resizeGestureActive = false;
        _dragCandidate = false;
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        // The preview handlers own the gesture so child controls can still receive ordinary clicks.
    }

    private ResizeEdge GetResizeEdge(System.Windows.Point position)
    {
        var verticalOnly = _viewModel.Expanded || SettingsPanel.Visibility == Visibility.Visible;
        var edge = ResizeEdge.None;
        if (!verticalOnly && position.X <= ResizeBorderThickness) edge |= ResizeEdge.Left;
        if (!verticalOnly && position.X >= ActualWidth - ResizeBorderThickness) edge |= ResizeEdge.Right;
        if (position.Y <= ResizeBorderThickness) edge |= ResizeEdge.Top;
        if (position.Y >= ActualHeight - ResizeBorderThickness) edge |= ResizeEdge.Bottom;
        return edge;
    }

    private static System.Windows.Input.Cursor CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => WpfCursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => WpfCursors.SizeNS,
        ResizeEdge.Left | ResizeEdge.Top or ResizeEdge.Right | ResizeEdge.Bottom => WpfCursors.SizeNWSE,
        ResizeEdge.Right | ResizeEdge.Top or ResizeEdge.Left | ResizeEdge.Bottom => WpfCursors.SizeNESW,
        _ => WpfCursors.Arrow
    };

    private System.Windows.Point GetScreenPoint(System.Windows.Point clientPoint)
    {
        var device = PointToScreen(clientPoint);
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformFromDevice.Transform(device) ?? device;
    }

    private void ApplyManualResize(System.Windows.Point screenPoint)
    {
        var delta = new ResizeVector(screenPoint.X - _resizeStartScreen.X, screenPoint.Y - _resizeStartScreen.Y);
        var start = new ResizeBounds(_resizeStartBounds.Left, _resizeStartBounds.Top, _resizeStartBounds.Width, _resizeStartBounds.Height);
        var handle = ToResizeHandle(_resizeEdge);

        if (_viewModel.Expanded || SettingsPanel.Visibility == Visibility.Visible)
        {
            var bounds = ManualResizeGeometry.ResizeVertical(start, delta, handle, _resizeWorkArea, MinHeight, MaxHeight);
            Top = bounds.Top;
            Height = bounds.Height;
            return;
        }

        var compactBounds = ManualResizeGeometry.ResizeCompact(start, delta, handle, _resizeWorkArea, CompactMinWidth, CompactMaxWidth);
        Left = compactBounds.Left;
        Top = compactBounds.Top;
        SetCompactSize(compactBounds.Width);
    }

    private ResizeWorkArea GetResizeWorkArea()
    {
        var topLeft = PointToScreen(new System.Windows.Point(0, 0));
        var bottomRight = PointToScreen(new System.Windows.Point(ActualWidth, ActualHeight));
        var windowRect = new NativeRect
        {
            Left = (int)Math.Floor(topLeft.X), Top = (int)Math.Floor(topLeft.Y),
            Right = (int)Math.Ceiling(bottomRight.X), Bottom = (int)Math.Ceiling(bottomRight.Y)
        };
        var monitor = MonitorFromRect(ref windowRect, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
            return new ResizeWorkArea(SystemParameters.WorkArea.Left, SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Width, SystemParameters.WorkArea.Height);

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        var workTopLeft = new System.Windows.Point(monitorInfo.Work.Left, monitorInfo.Work.Top);
        var workBottomRight = new System.Windows.Point(monitorInfo.Work.Right, monitorInfo.Work.Bottom);
        if (transform is { } deviceToDip)
        {
            workTopLeft = deviceToDip.Transform(workTopLeft);
            workBottomRight = deviceToDip.Transform(workBottomRight);
        }
        return new ResizeWorkArea(workTopLeft.X, workTopLeft.Y, workBottomRight.X - workTopLeft.X, workBottomRight.Y - workTopLeft.Y);
    }

    private static ResizeHandle ToResizeHandle(ResizeEdge edge) => (ResizeHandle)(int)edge;

    private void FinishManualResize(bool endGesture)
    {
        _manualResize = false;
        if (endGesture) _resizeGestureActive = false;
        _resizeEdge = ResizeEdge.None;
        ReleaseMouseCapture();
        Cursor = WpfCursors.Arrow;
        Save();
    }

    private static bool IsInteractive(DependencyObject source)
    {
        for (DependencyObject? current = source; current is not null; current = GetParent(current))
            if (current is System.Windows.Controls.Primitives.ButtonBase or Selector or RangeBase or System.Windows.Controls.Primitives.TextBoxBase or System.Windows.Controls.Primitives.ScrollBar or Thumb or Popup) return true;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current) => current switch
    {
        System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D => System.Windows.Media.VisualTreeHelper.GetParent(current),
        _ => LogicalTreeHelper.GetParent(current)
    };
    private void ShowChrome(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_viewModel.Expanded) return;
        Chrome.IsHitTestVisible = true;
        Chrome.Opacity = 1;
    }

    private void HideChrome(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Chrome.Opacity = 0;
        Chrome.IsHitTestVisible = false;
    }
    private void ApplyWindowModeSize()
    {
        Chrome.Opacity = 0;
        Chrome.IsHitTestVisible = false;
        if (_viewModel.Expanded)
        {
            MinWidth = MaxWidth = 300;
            Width = 300;
            MinHeight = 260;
            MaxHeight = 720;
            Height = Math.Clamp(Height, 360, 720);
        }
        else
        {
            MinWidth = CompactMinWidth;
            MaxWidth = CompactMaxWidth;
            MinHeight = CompactMinWidth / CompactAspectRatio;
            MaxHeight = CompactMaxWidth / CompactAspectRatio;
            SetCompactSize(Math.Clamp(Width, CompactMinWidth, CompactMaxWidth));
        }
    }
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_adjustingCompactSize || _viewModel.Expanded || SettingsPanel.Visibility == Visibility.Visible) return;
        var width = e.WidthChanged
            ? Math.Clamp(ActualWidth, CompactMinWidth, CompactMaxWidth)
            : Math.Clamp(ActualHeight * CompactAspectRatio, CompactMinWidth, CompactMaxWidth);
        SetCompactSize(width);
    }
    private void SetCompactSize(double width)
    {
        _adjustingCompactSize = true;
        try
        {
            Width = width;
            Height = width / CompactAspectRatio;
        }
        finally { _adjustingCompactSize = false; }
    }
    private static bool IsLegacyDefaultCompactSize(AppSettings settings) =>
        (Math.Abs(settings.Width - 276d) < 0.01d && Math.Abs(settings.Height - 54d) < 0.01d) ||
        (Math.Abs(settings.Width - 156d) < 0.01d && Math.Abs(settings.Height - 52d) < 0.01d) ||
        (Math.Abs(settings.Width - 76d) < 0.01d && Math.Abs(settings.Height - 52d) < 0.01d);
    private void Save() => _store.Save(_settings with { Left = Left, Top = Top, Width = Width, Height = Height, IsExpanded = _viewModel.Expanded, IsTopmost = Topmost });
    private void OnClosing(object? sender, CancelEventArgs e) { _refreshTimer.Stop(); _shutdown.Cancel(); _client?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _tray?.Dispose(); Save(); }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const uint MonitorDefaultToNearest = 2;
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
}
