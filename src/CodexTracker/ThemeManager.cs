using System.Windows.Media;

namespace CodexTracker;

internal static class ThemeManager
{
    public static void Apply(string theme)
    {
        var dark = string.Equals(theme, "Escuro", StringComparison.OrdinalIgnoreCase);
        Set("Porcelain", dark ? "#202321" : "#F7F7F4");
        Set("Ink", dark ? "#F1F4F2" : "#202523");
        Set("SoftInk", dark ? "#AEB9B4" : "#59635F");
        Set("Sage", dark ? "#29443B" : "#D5E9E2");
        Set("Apricot", dark ? "#4C3B31" : "#F0DED0");
        Set("Lavender", dark ? "#37343D" : "#EAE8EF");
        Set("Accent", dark ? "#58C7A5" : "#0D8F6F");
        Set("GaugeTrack", dark ? "#46504C" : "#CCD5D1");
        Set("GlassSurface", dark ? "#FF202321" : "#FFF7F7F4");
        Set("GlassLavender", dark ? "#FF2A2E2C" : "#FFEAE8EF");
        Set("SettingsSurface", dark ? "#FF191C1B" : "#FFF2F2EE");
        Set("InputSurface", dark ? "#FF303633" : "#FFE7EBE8");
        Set("HoverSurface", dark ? "#FF39443F" : "#FFDCE7E2");
    }
    private static void Set(string key, string hex) => System.Windows.Application.Current.Resources[key] =
        new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
}
