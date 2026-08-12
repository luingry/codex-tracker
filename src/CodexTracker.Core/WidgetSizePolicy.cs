namespace CodexTracker.Core;

public enum WidgetVisualMode { Compact, Detailed, Settings }

public readonly record struct WidgetSize(double Width, double Height);

public sealed record WidgetModeSizes(WidgetSize? Compact = null, WidgetSize? Detailed = null, WidgetSize? Settings = null);

public static class WidgetSizePolicy
{
    public const double CompactAspectRatio = 62d / 52d;
    public const double CompactMinWidth = 62d;
    public const double CompactMaxWidth = 320d;
    public const double DetailedWidth = 300d;
    public const double DetailedMinHeight = 260d;
    public const double DetailedDefaultHeight = 360d;
    public const double DetailedMaxHeight = 720d;
    public const double SettingsMinHeight = 440d;
    public const double SettingsMaxHeight = 720d;

    public static WidgetSize Default(WidgetVisualMode mode) => mode switch
    {
        WidgetVisualMode.Compact => new(CompactMinWidth, CompactMinWidth / CompactAspectRatio),
        WidgetVisualMode.Detailed => new(DetailedWidth, DetailedDefaultHeight),
        WidgetVisualMode.Settings => new(DetailedWidth, SettingsMinHeight),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static WidgetSize Normalize(WidgetVisualMode mode, WidgetSize? size)
    {
        if (size is not { } value || !IsFinitePositive(value.Width) || !IsFinitePositive(value.Height)) return Default(mode);

        return mode switch
        {
            WidgetVisualMode.Compact => new(Math.Clamp(value.Width, CompactMinWidth, CompactMaxWidth), Math.Clamp(value.Width, CompactMinWidth, CompactMaxWidth) / CompactAspectRatio),
            WidgetVisualMode.Detailed => new(DetailedWidth, Math.Clamp(value.Height, DetailedMinHeight, DetailedMaxHeight)),
            WidgetVisualMode.Settings => new(DetailedWidth, Math.Clamp(value.Height, SettingsMinHeight, SettingsMaxHeight)),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    public static WidgetModeSizes NormalizeSlots(WidgetModeSizes? slots, bool isExpanded, WidgetSize legacySize)
    {
        var legacyMode = isExpanded ? WidgetVisualMode.Detailed : WidgetVisualMode.Compact;
        return new(
            Normalize(WidgetVisualMode.Compact, slots?.Compact ?? (legacyMode == WidgetVisualMode.Compact ? legacySize : null)),
            Normalize(WidgetVisualMode.Detailed, slots?.Detailed ?? (legacyMode == WidgetVisualMode.Detailed ? legacySize : null)),
            Normalize(WidgetVisualMode.Settings, slots?.Settings));
    }

    public static WidgetSize Get(WidgetModeSizes slots, WidgetVisualMode mode) => mode switch
    {
        WidgetVisualMode.Compact => Normalize(mode, slots.Compact),
        WidgetVisualMode.Detailed => Normalize(mode, slots.Detailed),
        WidgetVisualMode.Settings => Normalize(mode, slots.Settings),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    // UI mode switches must select the destination slot, never dimensions
    // temporarily held by the previous WPF layout constraints.
    public static WidgetSize SelectModeSize(WidgetModeSizes slots, WidgetVisualMode destination) => Get(slots, destination);

    public static WidgetModeSizes With(WidgetModeSizes slots, WidgetVisualMode mode, WidgetSize size)
    {
        var normalized = Normalize(mode, size);
        return mode switch
        {
            WidgetVisualMode.Compact => slots with { Compact = normalized },
            WidgetVisualMode.Detailed => slots with { Detailed = normalized },
            WidgetVisualMode.Settings => slots with { Settings = normalized },
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;
}
