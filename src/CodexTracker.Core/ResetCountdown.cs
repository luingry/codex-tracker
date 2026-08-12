namespace CodexTracker.Core;

public static class ResetCountdown
{
    public static string Format(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (resetsAt is null) return "reset indisponível";
        var span = resetsAt.Value - now;
        if (span <= TimeSpan.Zero) return "reiniciando agora";
        if (span.TotalDays >= 1) return $"reinicia em {(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"reinicia em {span.Hours}h {span.Minutes}m";
        return $"reinicia em {Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
    }
}
