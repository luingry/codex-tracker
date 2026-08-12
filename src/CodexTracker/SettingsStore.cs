using System.Text.Json;
using System.IO;
using CodexTracker.Core;

namespace CodexTracker;

public sealed record AppSettings(double Left = 80, double Top = 80, double Width = 276, double Height = 54, bool IsExpanded = false, bool IsTopmost = true, string? CodexPath = null, decimal UsdBrl = 5.50m, string Theme = "Claro", string CurrencyCode = "BRL");
public sealed class SettingsStore
{
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexTracker", "settings.json");
    public AppSettings Load() { try { return Normalize(JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new()); } catch { return new(); } }
    public void Save(AppSettings settings) { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(Normalize(settings), new JsonSerializerOptions { WriteIndented = true })); }
    public static string NormalizeCurrency(string? currencyCode) => CurrencyPresentation.Normalize(currencyCode);
    private static AppSettings Normalize(AppSettings settings) => settings with { CurrencyCode = CurrencyPresentation.Normalize(settings.CurrencyCode) };
}
