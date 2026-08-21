namespace CodexTracker.Core;

/// <summary>
/// Decides when the Codex desktop unread index can authoritatively reconcile
/// tracker completions. The desktop may remove a thread from its unread index
/// while its window is in the background, before the user has seen the result.
/// </summary>
public static class CodexDesktopUnreadReconciliation
{
    public static bool CanReconcileAbsentThreads(bool isIndexAvailable, bool isCodexForeground, bool isCodexMinimized) =>
        isIndexAvailable && isCodexForeground && !isCodexMinimized;
}
