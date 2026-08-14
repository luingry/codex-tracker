namespace CodexTracker.Core;

public static class ResetCountdown
{
    public static string Format(DateTimeOffset? resetsAt, DateTimeOffset now) => Format(resetsAt, now, "pt-BR");

    public static string Format(DateTimeOffset? resetsAt, DateTimeOffset now, string? languageCode)
    {
        var isEnglish = string.Equals(languageCode?.Trim(), "en-US", StringComparison.OrdinalIgnoreCase);
        if (resetsAt is null) return isEnglish ? "reset unavailable" : "reset indisponível";
        var span = resetsAt.Value - now;
        if (span <= TimeSpan.Zero) return isEnglish ? "resetting now" : "reiniciando agora";
        var prefix = isEnglish ? "resets in" : "reinicia em";
        if (span.TotalDays >= 1) return $"{prefix} {(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{prefix} {span.Hours}h {span.Minutes}m";
        return $"{prefix} {Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))}m";
    }
}
