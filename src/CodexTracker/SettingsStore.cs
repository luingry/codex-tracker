using System.Text.Json;
using System.IO;
using CodexTracker.Core;

namespace CodexTracker;

public sealed record AppSettings(double Left = 80, double Top = 80, double Width = 62, double Height = 52, bool IsExpanded = false, bool IsTopmost = true, string? CodexPath = null, decimal UsdBrl = 5.50m, string Theme = "Claro", string CurrencyCode = "BRL", WidgetModeSizes? ModeSizes = null, bool IsAgentListExpanded = false, string AccentColor = AccentPalette.DefaultBaseHex, string LanguageCode = LocalizationManager.DefaultLanguageCode);
public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexTracker", "settings.json");
    }

    public AppSettings Load() { try { return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new()); } catch { return Normalize(new()); } }
    public void Save(AppSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(Normalize(settings), new JsonSerializerOptions { WriteIndented = true })); }
    public static string NormalizeCurrency(string? currencyCode) => CurrencyPresentation.Normalize(currencyCode);
    public static AppSettings Normalize(AppSettings settings)
    {
        var slots = WidgetSizePolicy.NormalizeSlots(settings.ModeSizes, settings.IsExpanded, new(settings.Width, settings.Height));
        var active = WidgetSizePolicy.Get(slots, settings.IsExpanded ? WidgetVisualMode.Detailed : WidgetVisualMode.Compact);
        return settings with
        {
            CurrencyCode = CurrencyPresentation.Normalize(settings.CurrencyCode),
            AccentColor = AccentPalette.Normalize(settings.AccentColor),
            LanguageCode = LocalizationManager.NormalizeLanguage(settings.LanguageCode),
            Width = active.Width,
            Height = active.Height,
            ModeSizes = slots
        };
    }
}
