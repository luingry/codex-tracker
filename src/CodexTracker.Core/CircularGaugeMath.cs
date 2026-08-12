namespace CodexTracker.Core;

public static class CircularGaugeMath
{
    public static double Clamp(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Clamp(value, 0, 100);

    public static double SweepAngle(double value) => Clamp(value) * 3.6;

    public static bool IsFullCircle(double value) => Clamp(value) >= 100;
}
