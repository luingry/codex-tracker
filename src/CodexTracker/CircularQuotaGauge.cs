using System.Windows;
using System.Windows.Media;
using System.Windows.Automation.Peers;
using CodexTracker.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CodexTracker;

public sealed class CircularQuotaGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(CircularQuotaGauge), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CircularQuotaGauge), new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
        nameof(ProgressBrush), typeof(Brush), typeof(CircularQuotaGauge), new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(CircularQuotaGauge), new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ProgressBrush { get => (Brush)GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty, value); }
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var size = Math.Min(ActualWidth, ActualHeight);
        var thickness = Math.Max(1, Math.Min(StrokeThickness, size / 3));
        if (size <= thickness) return;

        var radius = (size - thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var trackPen = new Pen(TrackBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var value = CircularGaugeMath.Clamp(Value);
        if (value <= 0) return;
        var progressPen = new Pen(ProgressBrush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (CircularGaugeMath.IsFullCircle(value))
        {
            drawingContext.DrawEllipse(null, progressPen, center, radius, radius);
            return;
        }

        const double startDegrees = -90;
        var sweepDegrees = CircularGaugeMath.SweepAngle(value);
        var start = PointOnCircle(center, radius, startDegrees);
        var end = PointOnCircle(center, radius, startDegrees + sweepDegrees);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweepDegrees > 180, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, progressPen, geometry);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    private static Point PointOnCircle(Point center, double radius, double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
