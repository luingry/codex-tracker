using System.Text.Json;

namespace CodexTracker.Core;

/// <summary>
/// Reads the Codex desktop's authoritative local unread-thread list without
/// mutating its Electron state. A thread absent from this list has been read
/// in the desktop client, including when it was opened outside the tracker.
/// </summary>
public sealed record CodexDesktopUnreadThreads(bool IsAvailable, IReadOnlyCollection<string> ThreadIds);

public sealed class CodexDesktopUnreadThreadIndex
{
    private const string PersistedStateProperty = "electron-persisted-atom-state";
    private const string UnreadThreadsProperty = "unread-thread-ids-by-host-v1";
    private readonly string _statePath;

    public CodexDesktopUnreadThreadIndex(string? statePath = null)
    {
        _statePath = statePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", ".codex-global-state.json");
    }

    public CodexDesktopUnreadThreads Read()
    {
        try
        {
            using var stream = new FileStream(_statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (!root.TryGetProperty(PersistedStateProperty, out var persisted) || persisted.ValueKind != JsonValueKind.Object ||
                !persisted.TryGetProperty(UnreadThreadsProperty, out var unreadByHost) || unreadByHost.ValueKind != JsonValueKind.Object)
                return Unavailable();

            var threadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (unreadByHost.TryGetProperty("local", out var localUnread))
            {
                if (localUnread.ValueKind != JsonValueKind.Array) return Unavailable();
                foreach (var item in localUnread.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String) continue;
                    var threadId = item.GetString();
                    if (threadId is { Length: > 0 }) threadIds.Add(threadId.Trim());
                }
            }
            return new CodexDesktopUnreadThreads(true, threadIds);
        }
        catch (IOException) { return Unavailable(); }
        catch (UnauthorizedAccessException) { return Unavailable(); }
        catch (JsonException) { return Unavailable(); }
    }

    private static CodexDesktopUnreadThreads Unavailable() => new(false, Array.Empty<string>());
}
