namespace CodexTracker.Core;
public static class SanitizedLogger
{
    public static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexTracker", "logs", "codex-tracker.log");
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 512_000) File.Move(LogPath, LogPath + ".1", true);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message.Replace(home, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase).Replace(Environment.NewLine, " ")}\n");
        }
        catch { }
    }
}
