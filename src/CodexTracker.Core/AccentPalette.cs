using System.Globalization;

namespace CodexTracker.Core;

public sealed record AccentThemePalette(string BaseHex, string AccentHex, string SoftHex, string HoverHex, string GlowHex);

public static class AccentPalette
{
    public const string DefaultBaseHex = "#0D8F6F";

    public static string Normalize(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        if (candidate.StartsWith("#", StringComparison.Ordinal)) candidate = candidate.Substring(1);
        if (candidate.Length != 6 || !int.TryParse(candidate, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
            return DefaultBaseHex;
        return "#" + candidate.ToUpperInvariant();
    }

    public static AccentThemePalette Create(string? baseHex, bool dark)
    {
        var normalized = Normalize(baseHex);
        var seed = Parse(normalized);
        var surface = dark ? new Rgb(0x20, 0x23, 0x21) : new Rgb(0xF7, 0xF7, 0xF4);
        var accent = EnsureContrast(seed, surface, dark ? new Rgb(255, 255, 255) : new Rgb(0, 0, 0), 4.5);
        var soft = dark
            ? Mix(seed, surface, 0.70)
            : Mix(seed, surface, 0.82);
        var hover = dark
            ? Mix(seed, new Rgb(0x30, 0x36, 0x33), 0.78)
            : Mix(seed, new Rgb(0xE7, 0xEB, 0xE8), 0.84);
        var glow = Mix(accent, new Rgb(255, 255, 255), dark ? 0.18 : 0.12);
        return new(normalized, ToHex(accent), ToHex(soft), ToHex(hover), ToHex(glow));
    }

    public static double ContrastRatio(string firstHex, string secondHex)
    {
        var first = RelativeLuminance(Parse(Normalize(firstHex)));
        var second = RelativeLuminance(Parse(Normalize(secondHex)));
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static Rgb EnsureContrast(Rgb source, Rgb surface, Rgb target, double ratio)
    {
        if (ContrastRatio(source, surface) >= ratio) return source;
        var low = 0d;
        var high = 1d;
        for (var index = 0; index < 24; index++)
        {
            var amount = (low + high) / 2d;
            if (ContrastRatio(Mix(source, target, amount), surface) >= ratio) high = amount;
            else low = amount;
        }
        return Mix(source, target, high);
    }

    private static double ContrastRatio(Rgb first, Rgb second)
    {
        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance(Rgb color) =>
        0.2126 * Linearize(color.Red) + 0.7152 * Linearize(color.Green) + 0.0722 * Linearize(color.Blue);

    private static double Linearize(byte component)
    {
        var value = component / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Rgb Mix(Rgb source, Rgb target, double targetWeight) => new(
        MixComponent(source.Red, target.Red, targetWeight),
        MixComponent(source.Green, target.Green, targetWeight),
        MixComponent(source.Blue, target.Blue, targetWeight));

    private static byte MixComponent(byte source, byte target, double targetWeight) =>
        (byte)Math.Round(source + (target - source) * targetWeight, MidpointRounding.AwayFromZero);

    private static Rgb Parse(string normalized) => new(
        byte.Parse(normalized.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(normalized.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(normalized.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static string ToHex(Rgb color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}
