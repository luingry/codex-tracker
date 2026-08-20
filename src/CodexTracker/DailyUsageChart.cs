using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using CodexTracker.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfToolTip = System.Windows.Controls.ToolTip;

namespace CodexTracker;

public sealed class DailyUsageChart : FrameworkElement
{
    private readonly List<BarHit> _barHits = [];
    private readonly WpfToolTip _tooltip = new() { Placement = PlacementMode.Mouse, StaysOpen = true };
    private int _hoveredIndex = -1;

    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(IEnumerable), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(MediaBrush), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(MediaBrushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(MediaBrush), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(MediaBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(MediaBrush), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(MediaBrushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TooltipSurfaceProperty = DependencyProperty.Register(
        nameof(TooltipSurface), typeof(MediaBrush), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(MediaBrushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TooltipTextBrushProperty = DependencyProperty.Register(
        nameof(TooltipTextBrush), typeof(MediaBrush), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata(MediaBrushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CurrencyCodeProperty = DependencyProperty.Register(
        nameof(CurrencyCode), typeof(string), typeof(DailyUsageChart),
        new FrameworkPropertyMetadata("BRL", FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Series { get => (IEnumerable?)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public MediaBrush BarBrush { get => (MediaBrush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public MediaBrush TrackBrush { get => (MediaBrush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public MediaBrush LabelBrush { get => (MediaBrush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public MediaBrush TooltipSurface { get => (MediaBrush)GetValue(TooltipSurfaceProperty); set => SetValue(TooltipSurfaceProperty, value); }
    public MediaBrush TooltipTextBrush { get => (MediaBrush)GetValue(TooltipTextBrushProperty); set => SetValue(TooltipTextBrushProperty, value); }
    public string CurrencyCode { get => (string)GetValue(CurrencyCodeProperty); set => SetValue(CurrencyCodeProperty, value); }

    public DailyUsageChart()
    {
        IsHitTestVisible = true;
        _tooltip.SetResourceReference(StyleProperty, "TokenUsageToolTip");
        ToolTip = _tooltip;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { _hoveredIndex = -1; _tooltip.IsOpen = false; InvalidateVisual(); };
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(MediaBrushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var values = ReadValues();
        var days = Math.Max(DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month), values.Count);
        var maximum = values.Count == 0 ? 0 : values.Max(value => value.Tokens);
        const double labelHeight = 14;
        var chartHeight = Math.Max(1, ActualHeight - labelHeight);
        var gap = 1.5;
        var width = Math.Max(1.5, (ActualWidth - gap * (days - 1)) / days);
        var baseline = new MediaPen(TrackBrush, 1);
        drawingContext.DrawLine(baseline, new WpfPoint(0, chartHeight - .5), new WpfPoint(ActualWidth, chartHeight - .5));

        _barHits.Clear();
        for (var index = 0; index < days; index++)
        {
            var value = index < values.Count ? values[index] : DailyPoint.Zero(index + 1);
            var height = maximum <= 0 ? 2 : Math.Max(2, value.Tokens / maximum * (chartHeight - 3));
            var x = index * (width + gap);
            var brush = value.Tokens > 0 ? BarBrush : TrackBrush;
            var barRect = new Rect(x, chartHeight - height, width, height);
            drawingContext.DrawRoundedRectangle(brush, null, barRect, 1.5, 1.5);
            _barHits.Add(new(new Rect(x, 0, Math.Max(width, width + gap), chartHeight), value));
        }

        var typeface = new Typeface(new System.Windows.Media.FontFamily("./assets/fonts/#Source Sans 3"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        DrawLabel(drawingContext, "1", 0, chartHeight + 2, typeface);
        var last = days.ToString(CultureInfo.InvariantCulture);
        var formatted = CreateText(last, typeface);
        drawingContext.DrawText(formatted, new WpfPoint(Math.Max(0, ActualWidth - formatted.Width), chartHeight + 2));
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new PointHitTestResult(this, hitTestParameters.HitPoint);

    private List<DailyPoint> ReadValues()
    {
        var result = new List<DailyPoint>();
        if (Series is null) return result;
        var fallbackDay = 1;
        foreach (var item in Series)
        {
            if (item is null) { result.Add(DailyPoint.Zero(fallbackDay++)); continue; }
            if (TryNumber(item, out var direct)) { result.Add(new(fallbackDay++, Math.Max(0, direct), 0, 0, TokenUsageBreakdown.Zero)); continue; }
            var type = item.GetType();
            var dayValue = type.GetProperty("Day")?.GetValue(item);
            var day = dayValue is DateTime date ? date.Day : fallbackDay;
            var tokenProperty = type.GetProperty("Tokens") ?? type.GetProperty("Value") ?? type.GetProperty("Usage");
            _ = TryNumber(tokenProperty?.GetValue(item), out var tokens);
            _ = TryDecimal(type.GetProperty("UsdCost")?.GetValue(item), out var usd);
            _ = TryDecimal(type.GetProperty("BrlCost")?.GetValue(item), out var brl);
            var breakdown = type.GetProperty("Breakdown")?.GetValue(item) as TokenUsageBreakdown ?? TokenUsageBreakdown.Zero;
            result.Add(new(day, Math.Max(0, tokens), Math.Max(0, usd), Math.Max(0, brl), breakdown));
            fallbackDay++;
        }
        return result;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        var next = _barHits.FindIndex(hit => hit.Bounds.Contains(position));
        if (next == _hoveredIndex) return;
        _hoveredIndex = next;
        if (next >= 0) ShowTooltip(_barHits[next]);
        else _tooltip.IsOpen = false;
        InvalidateVisual();
    }

    private void ShowTooltip(BarHit hit)
    {
        var exchangeRate = hit.Point.UsdCost > 0 ? hit.Point.BrlCost / hit.Point.UsdCost : 0;
        _tooltip.Content = TokenUsageTooltip.Create(LocalizationManager.Format("DayNumber", hit.Point.Day), hit.Point.Breakdown, true, exchangeRate, CurrencyCode);
        _tooltip.IsOpen = true;
    }

    private static bool TryNumber(object? value, out double number)
    {
        try { number = value is null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture); return value is not null; }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException) { number = 0; return false; }
    }

    private static bool TryDecimal(object? value, out decimal number)
    {
        try { number = value is null ? 0 : Convert.ToDecimal(value, CultureInfo.InvariantCulture); return value is not null; }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException) { number = 0; return false; }
    }

    private void DrawLabel(DrawingContext context, string text, double x, double y, Typeface typeface) =>
        context.DrawText(CreateText(text, typeface), new WpfPoint(x, y));

    private FormattedText CreateText(string text, Typeface typeface) => new(
        text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, typeface, 8, LabelBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private sealed record DailyPoint(int Day, double Tokens, decimal UsdCost, decimal BrlCost, TokenUsageBreakdown Breakdown)
    {
        public static DailyPoint Zero(int day) => new(day, 0, 0, 0, TokenUsageBreakdown.Zero);
    }
    private sealed record BarHit(Rect Bounds, DailyPoint Point);
}
