using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;
using CodexTracker.Core;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace CodexTracker;

public sealed class DailyUsageChart : FrameworkElement
{
    private readonly List<BarHit> _barHits = [];
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
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => { _hoveredIndex = -1; InvalidateVisual(); };
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

        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        DrawLabel(drawingContext, "1", 0, chartHeight + 2, typeface);
        var last = days.ToString(CultureInfo.InvariantCulture);
        var formatted = CreateText(last, typeface);
        drawingContext.DrawText(formatted, new WpfPoint(Math.Max(0, ActualWidth - formatted.Width), chartHeight + 2));
        if (_hoveredIndex >= 0 && _hoveredIndex < _barHits.Count)
            DrawTooltip(drawingContext, _barHits[_hoveredIndex], typeface);
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
            if (TryNumber(item, out var direct)) { result.Add(new(fallbackDay++, Math.Max(0, direct), 0, 0)); continue; }
            var type = item.GetType();
            var dayValue = type.GetProperty("Day")?.GetValue(item);
            var day = dayValue is DateTime date ? date.Day : fallbackDay;
            var tokenProperty = type.GetProperty("Tokens") ?? type.GetProperty("Value") ?? type.GetProperty("Usage");
            _ = TryNumber(tokenProperty?.GetValue(item), out var tokens);
            _ = TryDecimal(type.GetProperty("UsdCost")?.GetValue(item), out var usd);
            _ = TryDecimal(type.GetProperty("BrlCost")?.GetValue(item), out var brl);
            result.Add(new(day, Math.Max(0, tokens), Math.Max(0, usd), Math.Max(0, brl)));
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
        InvalidateVisual();
    }

    private void DrawTooltip(DrawingContext context, BarHit hit, Typeface typeface)
    {
        var tokenText = TokenPresentation.Format((long)Math.Round(hit.Point.Tokens));
        var costText = CurrencyPresentation.FormatCost(hit.Point.UsdCost, hit.Point.BrlCost, CurrencyCode);
        var lines = new[] { $"Dia {hit.Point.Day}", tokenText, costText };
        var formatted = lines.Select(line => CreateTooltipText(line, typeface)).ToArray();
        var boxWidth = Math.Max(68, formatted.Max(line => line.Width) + 14);
        var boxHeight = 49d;
        var x = Net48Compatibility.Clamp(hit.Bounds.X + hit.Bounds.Width / 2 - boxWidth / 2, 0, Math.Max(0, ActualWidth - boxWidth));
        var y = 2d;
        var box = new Rect(x, y, boxWidth, boxHeight);
        context.DrawRoundedRectangle(TooltipSurface, null, box, 6, 6);
        for (var index = 0; index < formatted.Length; index++)
            context.DrawText(formatted[index], new WpfPoint(x + 7, y + 5 + index * 14));
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

    private FormattedText CreateTooltipText(string text, Typeface typeface) => new(
        text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, typeface, 9, TooltipTextBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private sealed record DailyPoint(int Day, double Tokens, decimal UsdCost, decimal BrlCost)
    {
        public static DailyPoint Zero(int day) => new(day, 0, 0, 0);
    }
    private sealed record BarHit(Rect Bounds, DailyPoint Point);
}
