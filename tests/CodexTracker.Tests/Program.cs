using System.IO;
using System.Text.Json;
using CodexTracker.Core;
using CodexTracker;

var trayOnlyExtendedStyle = TrayOnlyWindowPolicy.ToTrayOnlyExtendedStyle(TrayOnlyWindowPolicy.AppWindowExtendedStyle | 0x00000008L);
Assert((trayOnlyExtendedStyle & TrayOnlyWindowPolicy.ToolWindowExtendedStyle) != 0 && (trayOnlyExtendedStyle & TrayOnlyWindowPolicy.AppWindowExtendedStyle) == 0 && (trayOnlyExtendedStyle & 0x00000008L) != 0, "tray-only policy adds WS_EX_TOOLWINDOW, removes WS_EX_APPWINDOW, and preserves unrelated extended styles");

var compactGauge = CompactGaugeLayoutPolicy.ForWindow(new WidgetSize(62, 52));
Assert(compactGauge == new CompactGaugeLayout(42, 38), "minimum compact layout keeps the circular background exactly 4 DIP smaller than the 42 DIP gauge");
Assert(CompactGaugeLayoutPolicy.ForWindow(new WidgetSize(100, 100 / (62d / 52d))) == new CompactGaugeLayout(100 / (62d / 52d) - 10, 100 / (62d / 52d) - 14), "resized compact layout derives both vector circles from the final window height without Viewbox scaling");
Assert(NearlyEqual(CompactGaugeLayoutPolicy.FontSizeForWindow(new(62, 52)), 15.18) && NearlyEqual(CompactGaugeLayoutPolicy.FontSizeForWindow(new(81, 0)), 15.18 * 81d / 62d) && NearlyEqual(CompactGaugeLayoutPolicy.FontSizeForWindow(new(100, 0)), 15.18 * 100d / 62d), "compact font uses the 15% larger 15.18 DIP base size and scales proportionally with the widget width");
Assert(BackdropCompositionPolicy.ForMode(WidgetVisualMode.Compact) == new BackdropComposition(BackdropNonClientRendering.Disabled, BackdropCornerPreference.DoNotRound) && BackdropCompositionPolicy.ForMode(WidgetVisualMode.Detailed) == new BackdropComposition(BackdropNonClientRendering.Enabled, BackdropCornerPreference.Round) && BackdropCompositionPolicy.ForMode(WidgetVisualMode.Settings) == new BackdropComposition(BackdropNonClientRendering.Enabled, BackdropCornerPreference.Round), "backdrop composition disables non-client shadow composition only in compact mode");

var lightAccent = AccentPalette.Create("#FFB000", false);
var darkAccent = AccentPalette.Create("#FFB000", true);
Assert(lightAccent.BaseHex == "#FFB000" && darkAccent.BaseHex == "#FFB000", "accent palette preserves the user's canonical seed color across themes");
Assert(AccentPalette.ContrastRatio(lightAccent.AccentHex, "#F7F7F4") >= 4.5 && AccentPalette.ContrastRatio(darkAccent.AccentHex, "#202321") >= 4.5, "derived accent text remains readable on light and dark primary surfaces");
Assert(lightAccent.SoftHex != lightAccent.AccentHex && lightAccent.HoverHex != lightAccent.AccentHex && lightAccent.GlowHex != lightAccent.AccentHex, "a single accent seed derives distinct soft, hover and glow tonal roles");
Assert(AccentPalette.ContrastRatio(lightAccent.AgentMetadataHex, "#F7F7F4") >= 4.5 && AccentPalette.ContrastRatio(darkAccent.AgentMetadataHex, "#202321") >= 4.5 && AccentPalette.Saturation(lightAccent.AgentMetadataHex) < AccentPalette.Saturation(lightAccent.AccentHex) && AccentPalette.Saturation(darkAccent.AgentMetadataHex) < AccentPalette.Saturation(darkAccent.AccentHex), "agent model and effort receive a less saturated but contrast-safe accent role in both themes");
Assert(AccentPalette.Normalize("invalid") == AccentPalette.DefaultBaseHex && AccentPalette.Normalize("0d8f6f") == "#0D8F6F", "invalid accent settings fall back safely while valid hex colors normalize canonically");

LocalizationManager.Apply("en-US");
Assert(LocalizationManager.CurrentLanguageCode == "en-US" && LocalizationManager.Text("Settings") == "Settings" && LocalizationManager.TranslateKnown("Trabalhando") == "Working" && LocalizationManager.TranslateKnown("Conversa Codex") == "Codex conversation", "en-US localization translates static and known runtime agent text");
Assert(TokenPresentation.Format(1_234_567, "en-US") == "1.23 M" && CurrencyPresentation.FormatCost(12.5m, 0m, "USD", "en-US") == "US$ 12.50" && ResetCountdown.Format(new DateTimeOffset(2026, 8, 15, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), "en-US") == "resets in 1d 2h" && WeeklyForecastCalculator.FormatProjectedPercent(99.5, "en-US") == "99.5%", "en-US localization formats tokens, currency, reset countdown and forecast percentages with English conventions");
LocalizationManager.Apply("unsupported");
Assert(LocalizationManager.CurrentLanguageCode == "pt-BR" && LocalizationManager.Text("Settings") == "Configurações", "unsupported languages safely normalize to pt-BR");

var dualMonitorWorkAreas = new[] { new WidgetScreenRect(0, 0, 1920, 1080), new WidgetScreenRect(1920, 0, 3840, 1080) };
var secondarySavedBounds = new WidgetScreenRect(2877, 176, 2877 + 62, 176 + 52);
Assert(WidgetPlacementPolicy.Restore(secondarySavedBounds, dualMonitorWorkAreas) == secondarySavedBounds, "saved compact widget position on a secondary monitor remains unchanged instead of being clamped to the primary work area");
var leftMonitorWorkAreas = new[] { new WidgetScreenRect(-1920, 0, 0, 1080), new WidgetScreenRect(0, 0, 1920, 1080) };
var leftSavedBounds = new WidgetScreenRect(-1500, 176, -1500 + 62, 176 + 52);
Assert(WidgetPlacementPolicy.Restore(leftSavedBounds, leftMonitorWorkAreas) == leftSavedBounds, "saved widget position on a negative-coordinate monitor remains unchanged");
var partiallyVisibleBounds = new WidgetScreenRect(1890, 176, 1890 + 62, 176 + 52);
Assert(WidgetPlacementPolicy.Restore(partiallyVisibleBounds, dualMonitorWorkAreas) == partiallyVisibleBounds, "partially visible widget position is not unnecessarily moved");
var removedMonitorBounds = new WidgetScreenRect(5000, 176, 5000 + 62, 176 + 52);
Assert(WidgetPlacementPolicy.Restore(removedMonitorBounds, dualMonitorWorkAreas) == new WidgetScreenRect(3778, 176, 3840, 228), "offscreen widget falls back inside the nearest available work area");

var rankingNow = DateTimeOffset.Now;
var rankingCycleEnd = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
var rankingCycleStart = rankingCycleEnd.AddMinutes(-10080);
var rankingAnalytics = new UsageAnalytics(0, 99, 0, 0, 0, [new ModelUsage("month-model", 99, 0, true)], ModelTimeline:
[
    new(rankingNow.AddMinutes(-1), "day-model", 11, 0, false),
    new(rankingCycleStart.AddMinutes(1), "cycle-model", 50, 0, true),
    new(rankingCycleStart.AddMinutes(-1), "outside-cycle-model", 99, 0, true)
]);
var rankingViewModel = new MainViewModel();
rankingViewModel.Apply(new RateLimitSnapshot([new("codex:primary", "Weekly limit", 16, rankingCycleEnd, 10080)], null, null, null, rankingNow), rankingAnalytics);
Assert(rankingViewModel.Ranking.Single().Model == "month-model", "ranking preserves the monthly view as its default");
rankingViewModel.IsRankingDay = true;
Assert(rankingViewModel.Ranking.Single().Model == "day-model", "day ranking filters model usage to the current day");
rankingViewModel.IsRankingWeek = true;
Assert(rankingViewModel.Ranking.Any(row => row.Model == "cycle-model") && rankingViewModel.Ranking.All(row => row.Model != "outside-cycle-model"), "week ranking uses only the active Codex quota cycle, including usage before Monday and excluding usage outside the cycle");
rankingViewModel.ApplyQuota(new RateLimitSnapshot([], null, null, null, rankingNow));
Assert(rankingViewModel.Ranking.Count == 0, "week ranking is empty when the official active Codex quota cycle is unavailable");

var compactSize = WidgetSizePolicy.Normalize(WidgetVisualMode.Compact, new WidgetSize(999, 1));
Assert(compactSize.Width == 100 && Math.Abs(compactSize.Height * 62 - compactSize.Width * 52) < 0.001, "compact size clamps width at 100 and preserves the 62:52 ratio");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(1, 999)) == new WidgetSize(300, 720), "detailed size keeps fixed width and clamps visible height");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 300)) == new WidgetSize(300, 300) && WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 1)) == new WidgetSize(300, 260), "detailed preserves resized heights down to 260 and clamps below it");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Settings, new WidgetSize(double.NaN, 0)) == WidgetSizePolicy.Default(WidgetVisualMode.Settings), "invalid settings size falls back to its safe default");
Assert(WidgetSizePolicy.DetailedMaxHeightForContent(512.2) == 513 && WidgetSizePolicy.DetailedMaxHeightForContent(999) == WidgetSizePolicy.DetailedMaxHeight, "detailed maximum height follows rounded content height without exceeding the safety cap");
var independentSlots = WidgetSizePolicy.NormalizeSlots(new WidgetModeSizes(new(124, 10), new(300, 480), new(300, 600)), false, new(62, 52));
Assert(WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Compact) == new WidgetSize(100, 100 / (62d / 52d)) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Settings) == new WidgetSize(300, 600), "persisted compact widths above 100 clamp without changing detailed and settings slots");
var conceptualTransition = WidgetSizePolicy.With(independentSlots, WidgetVisualMode.Compact, new(200, 1));
Assert(WidgetSizePolicy.Get(conceptualTransition, WidgetVisualMode.Compact) == new WidgetSize(100, 100 / (62d / 52d)) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Settings) == new WidgetSize(300, 600), "compact manual resize saves only a normalized compact slot");
var compactDetailedCompact = new WidgetModeSizes(new(100, 1), new(300, 533), new(300, 600));
var restoredCompact = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Compact);
var restoredDetailed = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Detailed);
var restoredCompactAgain = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Compact);
Assert(restoredCompact == new WidgetSize(100, 100 / (62d / 52d)) && restoredDetailed == new WidgetSize(300, 533) && restoredCompactAgain == restoredCompact, "compact cap survives repeated compact-detailed-compact selection without transient detailed dimensions");
var legacyCompact = SettingsStore.Normalize(new AppSettings(Width: 124, Height: 99, IsExpanded: false));
Assert(WidgetSizePolicy.Get(legacyCompact.ModeSizes!, WidgetVisualMode.Compact) == new WidgetSize(100, 100 / (62d / 52d)) && WidgetSizePolicy.Get(legacyCompact.ModeSizes!, WidgetVisualMode.Detailed) == WidgetSizePolicy.Default(WidgetVisualMode.Detailed), "legacy compact JSON migrates and clamps its dimensions only into compact");
var legacyDetailed = SettingsStore.Normalize(new AppSettings(Width: 222, Height: 480, IsExpanded: true));
Assert(WidgetSizePolicy.Get(legacyDetailed.ModeSizes!, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(legacyDetailed.ModeSizes!, WidgetVisualMode.Compact) == WidgetSizePolicy.Default(WidgetVisualMode.Compact), "legacy detailed JSON migrates its dimensions only into detailed");
var missingSizes = SettingsStore.Normalize(new AppSettings(ModeSizes: null));
Assert(missingSizes.ModeSizes is not null && WidgetSizePolicy.Get(missingSizes.ModeSizes, WidgetVisualMode.Settings) == WidgetSizePolicy.Default(WidgetVisualMode.Settings), "absent mode slots receive safe defaults");
var serializedSettings = legacyDetailed with { ModeSizes = new WidgetModeSizes(new(100, 1), new(300, 500), new(300, 650)) };
var roundTrippedSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(SettingsStore.Normalize(serializedSettings)))!;
Assert(WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Compact) == new WidgetSize(100, 100 / (62d / 52d)) && WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Detailed) == new WidgetSize(300, 500) && WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Settings) == new WidgetSize(300, 650), "new JSON round trip retains all three size slots");

var settingsTestDirectory = Path.Combine(Path.GetTempPath(), "CodexTracker.Tests", Guid.NewGuid().ToString("N"));
var settingsTestPath = Path.Combine(settingsTestDirectory, "settings.json");
try
{
    var persistedSettings = new AppSettings(Left: 412.5, Top: 237.25, IsExpanded: true, IsTopmost: false, CodexPath: @"C:\\Tools\\codex.exe", UsdBrl: 5.89m, Theme: "Escuro", CurrencyCode: "USD", ModeSizes: new WidgetModeSizes(new(90, 1), new(300, 480), new(300, 620)), IsAgentListExpanded: true, AccentColor: "#FFB000", LanguageCode: "en-US");
    new SettingsStore(settingsTestPath).Save(persistedSettings);
    var reloadedSettings = new SettingsStore(settingsTestPath).Load();
    Assert(reloadedSettings.Left == 412.5 && reloadedSettings.Top == 237.25 && reloadedSettings.IsExpanded && !reloadedSettings.IsTopmost && reloadedSettings.CodexPath == @"C:\\Tools\\codex.exe" && reloadedSettings.UsdBrl == 5.89m && reloadedSettings.Theme == "Escuro" && reloadedSettings.CurrencyCode == "USD" && reloadedSettings.IsAgentListExpanded && reloadedSettings.AccentColor == "#FFB000" && reloadedSettings.LanguageCode == "en-US", "settings file round trip persists agent-list expansion, accent color and language without replacing other preferences");
}
finally
{
    if (Directory.Exists(settingsTestDirectory)) Directory.Delete(settingsTestDirectory, true);
}
Assert(!Directory.Exists(settingsTestDirectory), "temporary settings round-trip directory is removed after the test");
Assert(SettingsStore.Normalize(new AppSettings(AccentColor: "not-a-color")).AccentColor == AccentPalette.DefaultBaseHex, "invalid persisted accent colors migrate to the safe default");
Assert(SettingsStore.Normalize(new AppSettings(LanguageCode: "fr-FR")).LanguageCode == "pt-BR", "unsupported persisted languages migrate to pt-BR");

var agentRowsViewModel = new MainViewModel();
Assert(CodexThreadDeepLink.TryCreate("018f18cc-9ffc-7bb3-9a48-7a3df5372adc", out var validThreadLink) && validThreadLink!.AbsoluteUri == "codex://threads/018f18cc-9ffc-7bb3-9a48-7a3df5372adc", "thread deep links accept a canonical UUID and target exactly its Codex thread");
Assert(!CodexThreadDeepLink.TryCreate("codex://threads/018f18cc-9ffc-7bb3-9a48-7a3df5372adc", out _) && !CodexThreadDeepLink.TryCreate("not-a-uuid", out _) && !CodexThreadDeepLink.TryCreate(null, out _), "thread deep links reject non-UUID input before shell execution");
var mainWindowXaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "CodexTracker", "MainWindow.xaml"));
var indicatorStart = mainWindowXaml.IndexOf("x:Name=\"AgentIndicatorButton\"", StringComparison.Ordinal);
var indicatorEnd = mainWindowXaml.IndexOf("<Popup x:Name=\"AgentListPopup\"", indicatorStart, StringComparison.Ordinal);
var indicatorTemplate = indicatorStart >= 0 && indicatorEnd > indicatorStart ? mainWindowXaml.Substring(indicatorStart, indicatorEnd - indicatorStart) : string.Empty;
Assert(indicatorTemplate.Contains("<Trigger Property=\"IsMouseOver\" Value=\"True\">", StringComparison.Ordinal) && indicatorTemplate.Contains("TargetName=\"AgentCount\" Property=\"Visibility\" Value=\"Collapsed\"", StringComparison.Ordinal) && indicatorTemplate.Contains("TargetName=\"AgentArrow\" Property=\"Visibility\" Value=\"Visible\"", StringComparison.Ordinal) && !indicatorTemplate.Contains("<MultiDataTrigger>", StringComparison.Ordinal), "agent indicator hover has one direct IsMouseOver trigger that swaps the count for the chevron independently of open state");
var agentListStart = mainWindowXaml.IndexOf("<Popup x:Name=\"AgentListPopup\"", StringComparison.Ordinal);
var agentListEnd = mainWindowXaml.IndexOf("</Popup>", agentListStart, StringComparison.Ordinal);
var agentListTemplate = agentListStart >= 0 && agentListEnd > agentListStart ? mainWindowXaml.Substring(agentListStart, agentListEnd - agentListStart) : string.Empty;
Assert(agentListTemplate.Contains("x:Name=\"AgentListWrapper\" Width=\"288\" MaxHeight=\"350\" Padding=\"0,8\"", StringComparison.Ordinal) && agentListTemplate.Contains("x:Name=\"AgentRow\" Margin=\"0,2\"", StringComparison.Ordinal) && agentListTemplate.Contains("<Border Margin=\"{Binding Indent}\" Padding=\"15,6\">", StringComparison.Ordinal), "agent row hover and ripple span the full wrapper width while content keeps vertical breathing room and hierarchy indentation");
Assert(agentListTemplate.Contains("Text=\"{Binding ModelAndEffort}\"", StringComparison.Ordinal) && agentListTemplate.Contains("Foreground=\"{DynamicResource AgentMetadataAccent}\"", StringComparison.Ordinal), "agent model and effort bind to the contrast-safe muted accent resource instead of fixed opacity or generic secondary ink");
Assert(mainWindowXaml.Contains("x:Name=\"AccentColorButton\" Click=\"ChooseAccentColor\"", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"AccentColorSwatch\"", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"AccentColorValue\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Color=\"{DynamicResource AccentGlow}\"", StringComparison.Ordinal), "settings expose an accessible accent color picker and the agent ripple glow follows the derived palette");
Assert(mainWindowXaml.Contains("x:Name=\"LanguageBox\" SelectionChanged=\"PreviewLanguage\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Tag=\"pt-BR\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Tag=\"en-US\"", StringComparison.Ordinal) && mainWindowXaml.Contains("{DynamicResource Loc.Settings}", StringComparison.Ordinal), "settings expose pt-BR/en-US selection and static UI strings use dynamic localization resources");
var rankingPeriodStyleStart = mainWindowXaml.IndexOf("<Style TargetType=\"RadioButton\">", StringComparison.Ordinal);
var rankingPeriodStyleEnd = mainWindowXaml.IndexOf("</Style>", rankingPeriodStyleStart, StringComparison.Ordinal);
var rankingPeriodStyle = rankingPeriodStyleStart >= 0 && rankingPeriodStyleEnd > rankingPeriodStyleStart ? mainWindowXaml.Substring(rankingPeriodStyleStart, rankingPeriodStyleEnd - rankingPeriodStyleStart) : string.Empty;
Assert(rankingPeriodStyle.Contains("<Setter Property=\"Cursor\" Value=\"Hand\" />", StringComparison.Ordinal), "ranking day/week/month selector advertises an interactive hand cursor through its shared radio style");
var localizedKeys = System.Text.RegularExpressions.Regex.Matches(mainWindowXaml, @"\{DynamicResource Loc\.([A-Za-z0-9]+)\}").Cast<System.Text.RegularExpressions.Match>().Select(match => match.Groups[1].Value).Distinct().ToArray();
LocalizationManager.Apply("pt-BR");
Assert(localizedKeys.All(LocalizationManager.HasTextKey), "every XAML localization key exists in both language catalogs");
LocalizationManager.Apply("en-US");
Assert(localizedKeys.All(key => !string.IsNullOrWhiteSpace(LocalizationManager.Text(key))), "every XAML localization key resolves to non-empty en-US text");
LocalizationManager.Apply("pt-BR");
var appXaml = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "CodexTracker", "App.xaml"));
Assert(appXaml.Contains("<Style x:Key=\"SettingsLabelStyle\" TargetType=\"TextBlock\">", StringComparison.Ordinal) && appXaml.Contains("<Setter Property=\"FontSize\" Value=\"11\" />", StringComparison.Ordinal) && appXaml.Contains("<Setter Property=\"FontWeight\" Value=\"SemiBold\" />", StringComparison.Ordinal) && appXaml.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource SoftInk}\" />", StringComparison.Ordinal), "settings labels share a semantic typography and color style");
Assert(appXaml.Contains("Foreground=\"{TemplateBinding Foreground}\" Focusable=\"False\"", StringComparison.Ordinal) && appXaml.Split(new[] { "TextElement.Foreground=\"{TemplateBinding Foreground}\"" }, StringSplitOptions.None).Length >= 4 && appXaml.Contains("Margin=\"0,0,10,0\"", StringComparison.Ordinal), "combo selection, dropdown items and standard checkbox labels inherit the theme foreground while checkboxes keep comfortable spacing");
Assert(mainWindowXaml.Contains("Text=\"{DynamicResource Loc.DarkTheme}\" Style=\"{StaticResource SettingsLabelStyle}\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Text=\"{DynamicResource Loc.Language}\" Style=\"{StaticResource SettingsLabelStyle}\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Text=\"{DynamicResource Loc.CodexPath}\" Style=\"{StaticResource SettingsLabelStyle}\"", StringComparison.Ordinal), "settings field labels consistently use the shared semantic style");
var progressStyleStart = appXaml.IndexOf("<Style TargetType=\"ProgressBar\">", StringComparison.Ordinal);
var progressStyleEnd = appXaml.IndexOf("</Style>", progressStyleStart, StringComparison.Ordinal);
var progressStyle = progressStyleStart >= 0 && progressStyleEnd > progressStyleStart ? appXaml.Substring(progressStyleStart, progressStyleEnd - progressStyleStart) : string.Empty;
Assert(!progressStyle.Contains("WorkGlow", StringComparison.Ordinal) && !progressStyle.Contains("IsWorkAnimationEnabled", StringComparison.Ordinal) && mainWindowXaml.Contains("IsWorking=\"{Binding IsWorkAnimationEnabled}\"", StringComparison.Ordinal), "ranking progress bars stay static while the weekly circular quota gauges retain their dedicated work glow");
var reasoningGlowStart = mainWindowXaml.IndexOf("x:Name=\"ReasoningGlow\"", StringComparison.Ordinal);
var reasoningGlowEnd = mainWindowXaml.IndexOf("</DataTemplate>", reasoningGlowStart, StringComparison.Ordinal);
var reasoningGlowTemplate = reasoningGlowStart >= 0 && reasoningGlowEnd > reasoningGlowStart ? mainWindowXaml.Substring(reasoningGlowStart, reasoningGlowEnd - reasoningGlowStart) : string.Empty;
Assert(reasoningGlowTemplate.Contains("<LinearGradientBrush MappingMode=\"Absolute\" StartPoint=\"0,0.5\" EndPoint=\"64,0.5\">", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<LinearGradientBrush.Transform><TranslateTransform x:Name=\"ReasoningGlowTransform\" X=\"-64\" /></LinearGradientBrush.Transform>", StringComparison.Ordinal) && !reasoningGlowTemplate.Contains("LinearGradientBrush.RelativeTransform", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:0\" Value=\"-64\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:1.30\" Value=\"268\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:3.30\" Value=\"268\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<DataTrigger Binding=\"{Binding IsWorkAnimationEnabled}\" Value=\"True\">", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<StopStoryboard BeginStoryboardName=\"ReasoningGlowStoryboard\" />", StringComparison.Ordinal), "reasoning glow uses a fixed 64-DIP absolute band, a 1.30-second left-to-right sweep, two-second hold, and the work-animation/reduced-motion trigger");
var mainWindowCode = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "CodexTracker", "MainWindow.xaml.cs"));
Assert(mainWindowXaml.Contains("x:Name=\"CodexPathFallbackPanel\" Visibility=\"Collapsed\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Click=\"AutoDetect\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Click=\"TestPath\"", StringComparison.Ordinal), "manual Codex path UI is a collapsed fallback and does not expose the obsolete auto-detect or test actions");
Assert(mainWindowCode.Contains("var automaticallyDetectedPath = CodexExecutableDiscovery.Find(null);", StringComparison.Ordinal) && mainWindowCode.Contains("CodexPathFallbackPanel.Visibility = automaticallyDetectedPath is null ? Visibility.Visible : Visibility.Collapsed;", StringComparison.Ordinal) && mainWindowCode.Contains("PathBox.Text = automaticallyDetectedPath ?? _settings.CodexPath ?? \"\";", StringComparison.Ordinal), "settings show the manual Codex path fallback only when automatic discovery fails and otherwise keep the detected or persisted path available");
Assert(mainWindowCode.Contains("var manualCodexPath = CodexPathFallbackPanel.Visibility == Visibility.Visible", StringComparison.Ordinal) && mainWindowCode.Contains("? string.IsNullOrWhiteSpace(PathBox.Text) ? null : PathBox.Text", StringComparison.Ordinal) && mainWindowCode.Contains(": _settings.CodexPath;", StringComparison.Ordinal), "applying settings clears or accepts PathBox only while the fallback is visible and preserves the stored Codex path while it is hidden");
Assert(mainWindowCode.Contains("CreateTrayIcon(_settings.AccentColor)", StringComparison.Ordinal) && mainWindowCode.Contains("ColorTranslator.FromHtml(AccentPalette.Normalize(accentColor))", StringComparison.Ordinal) && mainWindowCode.Contains("CreateTray();", StringComparison.Ordinal), "tray fallback and recreated localized menu follow the persisted accent instead of retaining a fixed green");
Assert(mainWindowCode.Contains("previousIcon?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("previousMenu?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("_trayMenu?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("_trayIcon?.Dispose();", StringComparison.Ordinal), "repeated language previews replace and deterministically dispose tray icon and menu native resources");
Assert(mainWindowCode.Contains("LocalizationManager.Apply(_settings.LanguageCode);", StringComparison.Ordinal) && mainWindowCode.Contains("LanguageCode = language", StringComparison.Ordinal) && mainWindowCode.Contains("private void PreviewLanguage", StringComparison.Ordinal), "language preview, cancel restoration and apply persistence are wired through the settings lifecycle");
var refreshAgentsStart = mainWindowCode.IndexOf("private async Task RefreshAgentsAsync", StringComparison.Ordinal);
var refreshAgentsEnd = mainWindowCode.IndexOf("private void ToggleTopmost", refreshAgentsStart, StringComparison.Ordinal);
var refreshAgentsCode = refreshAgentsStart >= 0 && refreshAgentsEnd > refreshAgentsStart ? mainWindowCode.Substring(refreshAgentsStart, refreshAgentsEnd - refreshAgentsStart) : string.Empty;
Assert(refreshAgentsCode.Contains("else if (_settings.IsAgentListExpanded && !_viewModel.Expanded) _viewModel.IsAgentListOpen = true;", StringComparison.Ordinal), "agent refresh never opens the list while detailed mode is active");
var toggleDetailedStart = mainWindowCode.IndexOf("private void ToggleDetailed", StringComparison.Ordinal);
var toggleDetailedEnd = mainWindowCode.IndexOf("private void Settings", toggleDetailedStart, StringComparison.Ordinal);
var toggleDetailedCode = toggleDetailedStart >= 0 && toggleDetailedEnd > toggleDetailedStart ? mainWindowCode.Substring(toggleDetailedStart, toggleDetailedEnd - toggleDetailedStart) : string.Empty;
Assert(toggleDetailedCode.Contains("if (_viewModel.Expanded) _viewModel.IsAgentListOpen = false;", StringComparison.Ordinal) && toggleDetailedCode.Contains("else if (_settings.IsAgentListExpanded && _viewModel.HasActiveAgents)", StringComparison.Ordinal) && toggleDetailedCode.Contains("_viewModel.IsAgentListOpen = true;", StringComparison.Ordinal) && toggleDetailedCode.Contains("RepositionAgentListPopup();", StringComparison.Ordinal) && !toggleDetailedCode.Contains("IsAgentListExpanded =", StringComparison.Ordinal), "detailed mode closes only the physical agent popup and compact mode restores it only for the saved expanded preference with active agents");
var agentRowsNow = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
var rootAgent = new ActiveAgent("root", null, 0, false, "Agent", "Principal", "Lendo", "gpt-5.6-sol", "high", agentRowsNow.AddMinutes(-1), agentRowsNow);
var childAgent = new ActiveAgent("child", "root", 1, true, "Subagent", "Filho", "Implementando", "gpt-5.6-terra", "medium", agentRowsNow.AddSeconds(-30), agentRowsNow);
agentRowsViewModel.ApplyAgents([rootAgent], agentRowsNow, false);
agentRowsViewModel.ApplyAgents([rootAgent, childAgent], agentRowsNow.AddSeconds(1), true, animationsEnabled: true);
Assert(agentRowsViewModel.ActiveAgents.Count == 2 && !agentRowsViewModel.ActiveAgents.Single(row => row.ThreadId == "root").IsNew && agentRowsViewModel.ActiveAgents.Single(row => row.ThreadId == "child").IsNew, "agent refresh preserves existing rows and flags only a newly appeared agent for entry animation");
agentRowsViewModel.MarkNewAgentRowsStable();
Assert(agentRowsViewModel.ActiveAgents.All(row => !row.IsNew), "agent row entry flags clear after their one-time animation");
var reducedMotionAgents = new MainViewModel();
reducedMotionAgents.ApplyAgents([rootAgent], agentRowsNow, false, animationsEnabled: false);
reducedMotionAgents.ApplyAgents([rootAgent, childAgent], agentRowsNow.AddSeconds(1), true, animationsEnabled: false);
Assert(reducedMotionAgents.ActiveAgents.All(row => !row.IsNew), "reduced-motion preference suppresses new-row entry animation deterministically");

var resizeWorkArea = new ResizeWorkArea(-1920, 0, 1920, 1080);
var compactStart = new ResizeBounds(-1500, 200, 124, 104);
var compactLeft = ManualResizeGeometry.ResizeCompact(compactStart, new(-100, 0), ResizeHandle.Left, resizeWorkArea);
Assert(compactLeft.Right == compactStart.Right && compactLeft.Top == compactStart.Top, "left compact resize preserves the opposite right/top anchor on a negative-coordinate monitor");
var compactRight = ManualResizeGeometry.ResizeCompact(compactStart, new(100, 0), ResizeHandle.Right, resizeWorkArea);
Assert(compactRight.Left == compactStart.Left && compactRight.Top == compactStart.Top, "right compact resize preserves the opposite left/top anchor");
var compactTop = ManualResizeGeometry.ResizeCompact(compactStart, new(0, -50), ResizeHandle.Top, resizeWorkArea);
Assert(compactTop.Bottom == compactStart.Bottom && compactTop.Left == compactStart.Left, "top compact resize preserves the opposite bottom/left anchor");
var compactBottom = ManualResizeGeometry.ResizeCompact(compactStart, new(0, 50), ResizeHandle.Bottom, resizeWorkArea);
Assert(compactBottom.Top == compactStart.Top && compactBottom.Left == compactStart.Left, "bottom compact resize preserves the opposite top/left anchor");
foreach (var handle in new[] { ResizeHandle.Left | ResizeHandle.Top, ResizeHandle.Right | ResizeHandle.Top, ResizeHandle.Left | ResizeHandle.Bottom, ResizeHandle.Right | ResizeHandle.Bottom })
{
    var resized = ManualResizeGeometry.ResizeCompact(compactStart, new(handle.HasFlag(ResizeHandle.Left) ? -80 : 80, handle.HasFlag(ResizeHandle.Top) ? -40 : 40), handle, resizeWorkArea);
    Assert((!handle.HasFlag(ResizeHandle.Left) || resized.Right == compactStart.Right) && (!handle.HasFlag(ResizeHandle.Right) || resized.Left == compactStart.Left) && (!handle.HasFlag(ResizeHandle.Top) || resized.Bottom == compactStart.Bottom) && (!handle.HasFlag(ResizeHandle.Bottom) || resized.Top == compactStart.Top), "compact corner resize preserves each opposite corner anchor");
}
var limitedCompact = ManualResizeGeometry.ResizeCompact(new(-1800, 100, 124, 104), new(-1000, 0), ResizeHandle.Left, resizeWorkArea);
Assert(limitedCompact.Left == -1920 && limitedCompact.Right == -1676 && limitedCompact.Right == -1676, "compact work-area limit stops at the monitor edge without moving the opposite anchor");
var maxCompact = ManualResizeGeometry.ResizeCompact(compactStart, new(1000, 0), ResizeHandle.Right, resizeWorkArea);
Assert(maxCompact.Width == 320 && maxCompact.Left == compactStart.Left && maxCompact.Height * 62 == maxCompact.Width * 52, "compact maximum and 62:52 ratio preserve the opposite anchor");
var minCompact = ManualResizeGeometry.ResizeCompact(compactStart, new(-1000, 0), ResizeHandle.Right, resizeWorkArea);
Assert(minCompact.Width == 62 && minCompact.Left == compactStart.Left, "compact minimum width preserves the opposite anchor");
var detailedStart = new ResizeBounds(-1400, 300, 300, 240);
var detailedTop = ManualResizeGeometry.ResizeVertical(detailedStart, new(500, -100), ResizeHandle.Top, resizeWorkArea, 50, 620);
Assert(detailedTop.Left == detailedStart.Left && detailedTop.Width == detailedStart.Width && detailedTop.Bottom == detailedStart.Bottom, "detailed/settings resize is vertical-only and preserves the bottom anchor from top handle");
var detailedBottom = ManualResizeGeometry.ResizeVertical(detailedStart, new(-500, 1000), ResizeHandle.Bottom, resizeWorkArea, 50, 620);
Assert(detailedBottom.Left == detailedStart.Left && detailedBottom.Width == detailedStart.Width && detailedBottom.Top == detailedStart.Top && detailedBottom.Height == 620, "detailed/settings bottom resize keeps horizontal bounds and honors maximum height");
var detailedMinimum = ManualResizeGeometry.ResizeVertical(detailedStart, new(0, 1000), ResizeHandle.Top, resizeWorkArea, 50, 620);
Assert(detailedMinimum.Height == 50 && detailedMinimum.Bottom == detailedStart.Bottom, "detailed/settings minimum height preserves the opposite anchor");

var payload = JsonDocument.Parse("""
{
  "rateLimits": {
    "planType": "pro",
    "credits": { "balance": "18.5", "hasCredits": true, "unlimited": false },
    "primary": { "usedPercent": 42.5, "resetsAt": 1786550400, "windowDurationMins": 300 },
    "secondary": null, "individualLimit": null
  },
  "rateLimitResetCredits": { "availableCount": 2 },
  "rateLimitsByLimitId": {
    "codex": { "limitName": "Codex historical", "primary": { "usedPercent": 42.5, "resetsAt": 1786550400, "windowDurationMins": 300 } },
    "codex_bengalfox": { "limitName": "GPT-5.3 Codex Spark", "primary": { "usedPercent": 67, "resetsAt": 1787000000, "windowDurationMins": 10080 } }
  }
}
""").RootElement;
var snapshot = RateLimitParser.Parse(payload, DateTimeOffset.UtcNow);
Assert(snapshot.PlanType == "pro", "plan comes from rateLimits envelope");
Assert(!snapshot.Windows.Any(x => x.Id.Contains("secondary", StringComparison.Ordinal) || x.Id.Contains("individual", StringComparison.Ordinal)), "null secondary and individual limits do not crash or fabricate windows");
Assert(snapshot.Credits == "Credits: 18.5" && snapshot.ResetCredits == "Reset credits: 2", "credits are human normalized");
Assert(snapshot.Windows.Count == 2, "historical codex bucket deduplicates and model bucket remains");
Assert(snapshot.Windows.Any(x => x.Id == "codex:primary" && x.ResetsAt == DateTimeOffset.FromUnixTimeSeconds(1786550400)), "unix reset parses");
Assert(snapshot.Windows.Any(x => x.Label.StartsWith("GPT-5.3 Codex Spark", StringComparison.Ordinal)), "model bucket keeps parent name");
var sparse = RateLimitParser.Parse(JsonDocument.Parse("""{ "rateLimits": { "primary": { "usedPercent":55,"windowDurationMins":300 } }, "rateLimitsByLimitId": { "codex_bengalfox": { "limitName":"GPT-5.3 Codex Spark", "primary":{"usedPercent":70,"windowDurationMins":10080} } } }""").RootElement, DateTimeOffset.UtcNow);
var merged = RateLimitParser.Merge(snapshot, sparse);
Assert(merged.PlanType == "pro" && merged.Credits == "Credits: 18.5" && merged.Windows.Count == 2, "sparse update preserves metadata and buckets");
var weeklyOnly = RateLimitParser.Parse(JsonDocument.Parse("""{ "rateLimits": { "planType":"pro", "primary":{"usedPercent":9,"windowDurationMins":10080} } }""").RootElement, DateTimeOffset.UtcNow);
Assert(!weeklyOnly.Windows.Any(x => x.WindowDurationMins is >= 240 and <= 360), "missing five-hour window is absent rather than zero");
var officialRemainingFixture = RateLimitParser.Parse(JsonDocument.Parse("""{ "rateLimits": { "primary":{"usedPercent":16,"resetsAt":1787090315,"windowDurationMins":10080} }, "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":16,"windowDurationMins":10080}}} }""").RootElement, DateTimeOffset.UtcNow);
var officialWeekly = officialRemainingFixture.Windows.Single(x => x.Id == "codex:primary");
Assert(QuotaPresentation.FormatWeeklyRemaining(officialWeekly) == "84%", "official used=16 displays 84% remaining to match Codex UI");
Assert(QuotaPresentation.FormatWeeklyRemaining(new("codex:primary", "Weekly", 0, null, 10080)) == "100%", "full remaining quota stays legible in compact display");
Assert(CircularGaugeMath.Clamp(-1) == 0 && CircularGaugeMath.Clamp(101) == 100, "gauge clamps values to its 0-100 range");
Assert(CircularGaugeMath.SweepAngle(50) == 180 && CircularGaugeMath.SweepAngle(83) == 298.8, "gauge sweep follows remaining percent");
Assert(CircularGaugeMath.IsFullCircle(100) && !CircularGaugeMath.IsFullCircle(99.5), "gauge keeps a distinct full-circle path");
Assert(WeeklyForecastCalculator.Calculate(officialWeekly, DateTimeOffset.UtcNow).Status is not null, "forecast keeps internal usedPercent semantics");
var numericCredits = RateLimitParser.Parse(JsonDocument.Parse("""{ "rateLimits": { "credits": { "balance": 2.75, "hasCredits": true } } }""").RootElement, DateTimeOffset.UtcNow);
Assert(numericCredits.Credits == "Credits: 2.75", "numeric credit balance remains supported");
var nullCredits = RateLimitParser.Parse(JsonDocument.Parse("""{ "rateLimits": { "credits": { "balance": null, "hasCredits": false } } }""").RootElement, DateTimeOffset.UtcNow);
Assert(nullCredits.Credits == "Credits: unavailable", "null balance with no credits is explicit");
Assert(ResetCountdown.Format(DateTimeOffset.UtcNow.AddMinutes(32), DateTimeOffset.UtcNow).Contains("32m"), "countdown formats minutes");
Assert(ResetCountdown.Format(null, DateTimeOffset.UtcNow) == "reset indisponível", "countdown localizes unavailable");
Assert(ResetCountdown.Format(DateTimeOffset.UtcNow.AddHours(6).AddMinutes(7), DateTimeOffset.UtcNow).StartsWith("reinicia em 6h"), "countdown localizes reset");
Assert(CodexExecutableDiscovery.Candidates("C:\\custom\\codex.exe", "C:\\user").First() == "C:\\custom\\codex.exe", "configured path wins");
var nowForecast = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
var defaultUsageRoots = LocalUsageAnalyticsService.DefaultRoots("C:\\profile");
Assert(defaultUsageRoots.Count == 2 && defaultUsageRoots[0].EndsWith(".codex\\sessions") && defaultUsageRoots[1].EndsWith(".codex\\archived_sessions"), "default analytics includes active and archived Codex sessions");
Assert(CurrencyPresentation.Normalize("brl") == "BRL", "BRL currency code normalizes to BRL");
Assert(CurrencyPresentation.Normalize("usd") == "USD", "USD currency code normalizes to USD");
Assert(CurrencyPresentation.Normalize("EUR") == "BRL", "unsupported currency falls back to BRL");
Assert(CurrencyPresentation.FormatMonthlyCost(3.2m, 17.6m, "BRL") == "R$ 17,60", "BRL cost formats the analytics value converted with the configured rate");
Assert(CurrencyPresentation.FormatMonthlyCost(3.2m, 17.6m, "USD") == "US$ 3,20", "USD cost uses the base value without conversion");
Assert(CurrencyPresentation.FormatCost(.8m, 4.4m, "BRL") == "R$ 4,40", "BRL formats today cost");
Assert(CurrencyPresentation.FormatCost(.8m, 4.4m, "USD") == "US$ 0,80", "USD formats today cost without conversion");
var currencyViewModel = new MainViewModel();
var currencyReset = DateTimeOffset.UtcNow.AddDays(1);
var currencyTimelineAt = currencyReset.AddHours(-1);
var currencyAnalytics = new UsageAnalytics(10, 20, 2m, 10m, 100, [], .5m, 2.5m, [new(DateTime.Today, 10, .5m, 2.5m)], [new(currencyTimelineAt, 10, 1m)], 5m);
currencyViewModel.Apply(new RateLimitSnapshot([new("codex:primary", "Weekly", 10, currencyReset, 10080)], null, null, null, DateTimeOffset.UtcNow), currencyAnalytics, "BRL");
Assert(currencyViewModel.WeeklyCost == "R$ 5,00" && currencyViewModel.TodayCost == "R$ 2,50" && currencyViewModel.MonthCost == "R$ 10,00", "view model initially formats all retained costs in BRL");
currencyViewModel.SetCurrency("USD");
Assert(currencyViewModel.CurrencyCode == "USD" && currencyViewModel.WeeklyCost == "US$ 1,00" && currencyViewModel.TodayCost == "US$ 0,50" && currencyViewModel.MonthCost == "US$ 2,00", "currency change immediately reformats retained weekly, daily and monthly costs without analytics refresh");
var forecastViewModel = new MainViewModel();
forecastViewModel.ApplyQuota(new RateLimitSnapshot([new("codex:primary", "Weekly", 80, nowForecast.AddDays(6), 10080)], null, null, null, nowForecast));
Assert(forecastViewModel.Reset.StartsWith("reinicia em ", StringComparison.Ordinal) && !forecastViewModel.Reset.Contains("restante esta semana", StringComparison.OrdinalIgnoreCase), "weekly reset keeps only the reset countdown label");
Assert(forecastViewModel.IsExhaustionRisk && forecastViewModel.Forecast.StartsWith("Risco de esgotar antes do reset", StringComparison.Ordinal), "view model exposes the early exhaustion risk for conditional UI emphasis");
forecastViewModel.ApplyQuota(new RateLimitSnapshot([new("codex:primary", "Weekly", 10, nowForecast.AddDays(3), 10080)], null, null, null, nowForecast));
Assert(!forecastViewModel.IsExhaustionRisk, "forecast emphasis clears when quota should last until reset");
Assert(TokenPresentation.Format(999) == "999", "small token counts remain legible");
Assert(TokenPresentation.Format(1_000) == "1 mil", "one thousand tokens uses mil");
Assert(TokenPresentation.Format(1_000_000) == "1 mi", "one million tokens uses mi");
Assert(TokenPresentation.Format(1_000_000_000) == "1 bi", "one billion tokens uses bi");
Assert(TokenPresentation.Format(1_234_567) == "1,23 mi", "abbreviated tokens round to two decimal places in pt-BR");
var duration = 10080;
var durable = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, nowForecast.AddDays(3), duration), nowForecast);
Assert(durable.Status == "Deve durar até o reset" && NearlyEqual(durable.ProjectedPercent, 17.5) && durable.ExhaustsAt is null, "forecast reports exact durable projection");
var risky = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 80, nowForecast.AddDays(6), duration), nowForecast);
Assert(risky.Status == "Risco de esgotar antes do reset" && NearlyEqual(risky.ProjectedPercent, 560) && risky.ExhaustsAt == nowForecast.AddHours(6), "forecast reports exact early exhaustion");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 0, nowForecast.AddDays(3), duration), nowForecast).Status == "Dados insuficientes", "forecast handles zero usage");
var observed = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 23, nowForecast.AddDays(6).AddHours(5).AddMinutes(24), duration), nowForecast);
Assert(NearlyEqual(observed.ProjectedPercent, 207.741935483871) && NearlyEqualInstant(observed.ExhaustsAt, new DateTimeOffset(2026, 8, 15, 2, 16, 10, 435, TimeSpan.Zero)), "forecast reproduces the observed 208 percent risk and exhaustion instant");
var exhausted = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 100, nowForecast.AddDays(6), duration), nowForecast);
Assert(exhausted.Status == "Limite esgotado" && exhausted.ExhaustsAt == nowForecast, "forecast explicitly reports an exhausted quota");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", double.NaN, nowForecast.AddDays(6), duration), nowForecast).Status == "Dados insuficientes", "forecast rejects non-finite usage");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, null, duration), nowForecast).Status == "Dados insuficientes", "forecast rejects a missing reset");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, nowForecast.AddDays(6), null), nowForecast).Status == "Dados insuficientes", "forecast rejects a missing duration");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, nowForecast, duration), nowForecast).Status == "Dados insuficientes", "forecast rejects an elapsed reset");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, nowForecast.AddDays(7).AddSeconds(1), duration), nowForecast).Status == "Dados insuficientes", "forecast rejects a future window start");
Assert(WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 10, nowForecast.AddDays(7).AddMinutes(-1), duration), nowForecast).Status == "Dados insuficientes", "forecast rejects a one-minute sample");
var offsetNow = nowForecast.ToOffset(TimeSpan.FromHours(-3));
var utcForecast = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 23, nowForecast.AddDays(6), duration), nowForecast);
var localForecast = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 23, nowForecast.AddDays(6), duration), offsetNow);
Assert(localForecast.ProjectedPercent is double localProjected && NearlyEqual(utcForecast.ProjectedPercent, localProjected) && utcForecast.ExhaustsAt == localForecast.ExhaustsAt, "forecast is invariant across local and UTC offsets");
var roundedBoundary = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 50.02, nowForecast.AddDays(3.5), duration), nowForecast);
Assert(roundedBoundary.Status == "Deve durar até o reset" && WeeklyForecastCalculator.FormatProjectedPercent(roundedBoundary.ProjectedPercent!.Value) == "100,0%", "rounded 100 percent projection cannot contradict its status");
var roundedRisk = WeeklyForecastCalculator.Calculate(new("codex:primary", "Weekly", 50.245, nowForecast.AddDays(3.5), duration), nowForecast);
Assert(roundedRisk.Status == "Risco de esgotar antes do reset" && WeeklyForecastCalculator.FormatProjectedPercent(roundedRisk.ProjectedPercent!.Value) == "100,5%", "near-threshold risk keeps one decimal place without contradicting its status");
var mergeReset = nowForecast.AddDays(6);
var mergeCurrent = new RateLimitSnapshot([new("codex:primary", "Weekly", 23, mergeReset, duration)], "pro", null, null, nowForecast);
var sameCycleSparse = new RateLimitSnapshot([new("codex:primary", "Usage limit", 24, null, null)], null, null, null, nowForecast.AddMinutes(1), true);
var sameCycleWindow = RateLimitParser.Merge(mergeCurrent, sameCycleSparse).Windows.Single();
Assert(sameCycleWindow.UsedPercent == 24 && sameCycleWindow.ResetsAt == mergeReset && sameCycleWindow.WindowDurationMins == duration, "same-cycle sparse update preserves forecast timing");
var newCycleSparse = new RateLimitSnapshot([new("codex:primary", "Usage limit", 2, null, null)], null, null, null, nowForecast.AddMinutes(2), true);
var newCycleWindow = RateLimitParser.Merge(mergeCurrent, newCycleSparse).Windows.Single();
Assert(newCycleWindow.UsedPercent == 2 && newCycleWindow.ResetsAt is null && newCycleWindow.WindowDurationMins is null, "usage drop never inherits timing from the previous cycle");
var elapsedTimingSparse = new RateLimitSnapshot([new("codex:primary", "Usage limit", 24, null, null)], null, null, null, mergeReset.AddSeconds(1), true);
var elapsedTimingWindow = RateLimitParser.Merge(mergeCurrent, elapsedTimingSparse).Windows.Single();
Assert(elapsedTimingWindow.ResetsAt is null && elapsedTimingWindow.WindowDurationMins is null, "sparse update never inherits an elapsed reset");
var analyticsRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-analytics-" + Guid.NewGuid());
Directory.CreateDirectory(analyticsRoot);
File.WriteAllText(Path.Combine(analyticsRoot, "session.jsonl"), """
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-terra"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":20,"output_tokens":0,"total_tokens":100}}}}
{"timestamp":"2026-08-12T10:01:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"cached_input_tokens":30,"output_tokens":0,"total_tokens":150}}}}
{"timestamp":"2026-08-12T10:02:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":180,"cached_input_tokens":30,"output_tokens":0,"total_tokens":180}}}}
malformed
""");
var analyticsNow = new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);
var analytics = new LocalUsageAnalyticsService(() => analyticsNow).Read(5.5m, analyticsRoot);
Assert(analytics.MonthTokens == 180, "cumulative token snapshots use deltas, never 430");
Assert(analytics.TodayUsd == 0.0003825m && analytics.TodayBrl == 0.00210375m, "today cost uses the same priced token buckets and configured BRL rate as the month");
var dailySeries = analytics.DailySeries ?? throw new InvalidOperationException("daily series missing");
Assert(dailySeries.Count == 31 && dailySeries.Sum(x => x.Tokens) == analytics.MonthTokens, "current-month chart includes every calendar day and sums exactly to month usage");
Assert(dailySeries.Count(x => x.Tokens == 0) == 30 && dailySeries.Single(x => x.Day.Day == 12).Tokens == 180, "daily chart explicitly fills zero-usage days");
Assert(dailySeries.Sum(x => x.UsdCost) == analytics.MonthUsd && dailySeries.Sum(x => x.BrlCost) == analytics.MonthBrl, "daily chart costs sum exactly to monthly USD and BRL estimates");
var exactReset = new DateTimeOffset(2026, 8, 12, 15, 0, 0, TimeSpan.Zero);
var boundaryAnalytics = new UsageAnalytics(0, 0, 0, 0, 0, [], Timeline:
[
    new(exactReset.AddDays(-7).AddTicks(-1), 1, .01m),
    new(exactReset.AddDays(-7), 2, .02m),
    new(exactReset.AddMinutes(-1), 4, .04m),
    new(exactReset, 8, .08m)
], UsdBrl: 5m);
Assert(boundaryAnalytics.TokensInWindow(exactReset.AddDays(-7), exactReset) == 6, "weekly tokens use the exact inclusive start and exclusive official reset boundaries");
var boundaryEstimate = boundaryAnalytics.EstimateInWindow(exactReset.AddDays(-7), exactReset);
Assert(boundaryEstimate == new UsageWindowEstimate(6, .06m, .30m), "weekly cost uses the same exact event boundaries as weekly tokens");
Assert(CurrencyPresentation.FormatCost(boundaryEstimate.CostUsd, boundaryEstimate.CostBrl, "USD") == "US$ 0,06" && CurrencyPresentation.FormatCost(boundaryEstimate.CostUsd, boundaryEstimate.CostBrl, "BRL") == "R$ 0,30", "weekly estimate formats in the selected USD or BRL currency");
var offsetReset = exactReset.ToOffset(TimeSpan.FromHours(-3));
Assert(boundaryAnalytics.TokensInWindow(offsetReset.AddDays(-7), offsetReset) == 6, "weekly interval is invariant when the official reset is represented with another timezone offset");
Assert(boundaryAnalytics.TokensInWeeklyWindow(new("codex:primary", "Weekly", 50, offsetReset, 10080)) == 6, "weekly token counter integrates the official quota reset rather than calendar-day approximations");
Assert(boundaryAnalytics.TokensInWeeklyWindow(new("codex:primary", "Weekly", 50, null, 10080)) is null, "weekly token counter stays unavailable without an official reset boundary");
var retentionRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-timeline-retention-" + Guid.NewGuid());
Directory.CreateDirectory(retentionRoot);
var recentAt = DateTimeOffset.UtcNow.AddHours(-1);
var oldAt = DateTimeOffset.UtcNow.AddDays(-20);
File.WriteAllText(Path.Combine(retentionRoot, "retention.jsonl"),
    $"{{\"timestamp\":\"{oldAt:O}\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":10,\"total_tokens\":10}}}}}}}}\n" +
    $"{{\"timestamp\":\"{recentAt:O}\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":20,\"total_tokens\":20}}}}}}}}\n");
var retentionAnalytics = new LocalUsageAnalyticsService();
var retainedUsage = retentionAnalytics.Read(5.5m, retentionRoot);
Assert(retainedUsage.Timeline is { Count: 1 } && retainedUsage.Timeline[0].Tokens == 10 && retainedUsage.Timeline[0].CostUsd == 0, "exact timeline retains recent deltas while unknown models remain excluded from estimated cost");
Assert(retentionAnalytics.CachedTimelineEntryCount == 1, "cached timeline remains bounded independently of historical session age");
Directory.Delete(retentionRoot, true);
var componentRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-components-" + Guid.NewGuid());
Directory.CreateDirectory(componentRoot);
File.WriteAllText(Path.Combine(componentRoot, "components.jsonl"), """
{"type":"session_meta","payload":{"session_id":"components","id":"components","thread_source":"root"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-sol"}}
{"timestamp":"2026-08-12T10:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"cached_input_tokens":80,"output_tokens":10,"reasoning_output_tokens":5,"total_tokens":110}}}}
""");
var componentUsage = new LocalUsageAnalyticsService().Read(5.5m, componentRoot);
Assert(componentUsage.MonthTokens == 115, "processed tokens equal input plus output plus reasoning, independent of legacy total_tokens semantics");
Assert(componentUsage.MonthUsd == 0.00059m, "reasoning tokens use the output tariff while cached input remains an input subset");
Directory.Delete(componentRoot, true);
File.AppendAllText(Path.Combine(analyticsRoot, "session.jsonl"), "\n");
var cachedAnalytics = new LocalUsageAnalyticsService();
_ = cachedAnalytics.Read(5.5m, analyticsRoot);
Assert(cachedAnalytics.FilesParsedLastRead == 1, "initial analytics read parses the source file");
_ = cachedAnalytics.Read(5.5m, analyticsRoot);
Assert(cachedAnalytics.FilesParsedLastRead == 0, "unchanged analytics file is served from cache");
Assert(cachedAnalytics.BytesReadLastRead == 0, "unchanged analytics read consumes zero JSONL bytes");
var appendedLine = "\n{\"timestamp\":\"2026-08-12T10:03:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":200,\"cached_input_tokens\":30,\"output_tokens\":0,\"total_tokens\":200}}}}";
File.AppendAllText(Path.Combine(analyticsRoot, "session.jsonl"), appendedLine);
var appendedUsage = cachedAnalytics.Read(5.5m, analyticsRoot);
Assert(appendedUsage.MonthTokens == 200 && cachedAnalytics.BytesReadLastRead > 0, "append preserves totals after cache validation");
Assert(cachedAnalytics.CachedBucketCount <= 2, "analytics cache retains compact day-model buckets rather than events");
var partialRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-partial-" + Guid.NewGuid());
Directory.CreateDirectory(partialRoot);
var partialPath = Path.Combine(partialRoot, "active.jsonl");
File.WriteAllText(partialPath, "{\"timestamp\":\"2026-08-12T10:00:00Z\",\"payload\":{\"type\":\"turn_context\",\"model\":\"gpt-5.6-terra\"}}\n{\"timestamp\":\"2026-08-12T10:01:00Z\",\"payload\":");
var partialAnalytics = new LocalUsageAnalyticsService();
Assert(partialAnalytics.Read(5.5m, partialRoot).MonthTokens == 0, "partial JSONL record does not contribute before newline commit");
File.AppendAllText(partialPath, "{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":0,\"total_tokens\":10}}}}\n");
Assert(partialAnalytics.Read(5.5m, partialRoot).MonthTokens == 10, "completed partial record is rebuilt and counted exactly once");
Directory.Delete(partialRoot, true);
var rewriteRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-rewrite-" + Guid.NewGuid());
Directory.CreateDirectory(rewriteRoot);
var rewritePath = Path.Combine(rewriteRoot, "rewrite.jsonl");
var rewriteA = "{\"timestamp\":\"2026-08-12T10:00:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":10,\"total_tokens\":10}}}}\n";
var rewriteB = rewriteA.Replace("10", "20");
File.WriteAllText(rewritePath, rewriteA); var rewriteAnalytics = new LocalUsageAnalyticsService();
Assert(rewriteAnalytics.Read(5.5m, rewriteRoot).MonthTokens == 10, "rewrite fixture starts with original total");
File.WriteAllText(rewritePath, rewriteB); File.SetLastWriteTimeUtc(rewritePath, DateTime.UtcNow.AddSeconds(2));
Assert(rewriteAnalytics.Read(5.5m, rewriteRoot).MonthTokens == 20 && rewriteAnalytics.FilesRebuiltLastRead == 1, "same length rewrite invalidates cache");
File.WriteAllText(rewritePath, rewriteA); File.SetLastWriteTimeUtc(rewritePath, DateTime.UtcNow.AddSeconds(3));
Assert(rewriteAnalytics.Read(5.5m, rewriteRoot).MonthTokens == 10 && rewriteAnalytics.FilesRebuiltLastRead == 1, "truncate or rewrite rebuilds correctly");
var shorter = "{\"timestamp\":\"2026-08-12T10:00:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":5,\"total_tokens\":5}}}}\n";
File.WriteAllText(rewritePath, shorter); File.SetLastWriteTimeUtc(rewritePath, DateTime.UtcNow.AddSeconds(4));
Assert(rewriteAnalytics.Read(5.5m, rewriteRoot).MonthTokens == 5 && rewriteAnalytics.FilesRebuiltLastRead == 1, "genuine truncation rebuilds correctly");
File.AppendAllText(rewritePath, "{\"timestamp\":\"2026-08-12T10:01:00Z\",\"payload\":{\"type\":\"turn_context\",\"model\":\"gpt-5.6-terra-ação\"}}\n{\"timestamp\":\"2026-08-12T10:02:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":12,\"total_tokens\":12}}}}\n");
var utf8Usage = rewriteAnalytics.Read(5.5m, rewriteRoot);
Assert(utf8Usage.Models.Any(x => x.Model == "gpt-5.6-terra-ação" && x.Tokens == 7), "utf8 append preserves model attribution and token delta");
var grownRewrite = rewriteA + "{\"timestamp\":\"2026-08-12T10:02:00Z\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":30,\"total_tokens\":30}}}}\n";
File.WriteAllText(rewritePath, grownRewrite); File.SetLastWriteTimeUtc(rewritePath, DateTime.UtcNow.AddSeconds(5));
Assert(rewriteAnalytics.Read(5.5m, rewriteRoot).MonthTokens == 30 && rewriteAnalytics.FilesRebuiltLastRead == 1, "prefix rewrite with length growth rebuilds rather than appends");
Directory.Delete(rewriteRoot, true);
File.WriteAllText(Path.Combine(analyticsRoot, "switch.jsonl"), """
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"unknown-model"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":20,"cached_input_tokens":0,"output_tokens":0,"total_tokens":20}}}}
{"timestamp":"2026-08-12T10:01:00Z","payload":{"type":"turn_context","model":"gpt-5.6-luna"}}
{"timestamp":"2026-08-12T10:01:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":5,"cached_input_tokens":0,"output_tokens":0,"total_tokens":5}}}}
""");
analytics = new LocalUsageAnalyticsService().Read(5.5m, analyticsRoot);
Assert(analytics.Models.Any(x => x.Model == "unknown-model" && !x.Priced), "unknown model remains visible and unpriced");
Directory.Delete(analyticsRoot, true);
var forksRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-forks-" + Guid.NewGuid());
Directory.CreateDirectory(forksRoot);
File.WriteAllText(Path.Combine(forksRoot, "root.jsonl"), """
{"type":"session_meta","timestamp":"2026-08-12T10:00:00Z","payload":{"session_id":"s1","id":"s1","thread_source":"root"}}
{"type":"turn_context","timestamp":"2026-08-12T10:00:00Z","payload":{"model":"gpt-5.6-terra"}}
{"type":"event_msg","timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":180,"cached_input_tokens":0,"output_tokens":0,"total_tokens":180}}}}
""");
File.WriteAllText(Path.Combine(forksRoot, "fork.jsonl"), """
{"type":"session_meta","timestamp":"2026-08-12T10:00:00Z","payload":{"session_id":"s1","id":"f1","forked_from_id":"s1","thread_source":"subagent"}}
{"type":"session_meta","timestamp":"2026-08-12T10:00:00Z","payload":{"session_id":"s1","id":"s1","thread_source":"root"}}
{"type":"event_msg","timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":180,"cached_input_tokens":0,"output_tokens":0,"total_tokens":180}}}}
{"type":"turn_context","timestamp":"2026-08-12T10:00:01Z","payload":{"model":"gpt-5.6-sol"}}
{"type":"event_msg","timestamp":"2026-08-12T10:00:02Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":210,"cached_input_tokens":0,"output_tokens":0,"total_tokens":210}}}}
""");
var forked = new LocalUsageAnalyticsService().Read(5.5m, forksRoot);
Assert(forked.MonthTokens == 390 && forked.Models.Any(x => x.Model == "gpt-5.6-terra") && forked.Models.Any(x => x.Model == "gpt-5.6-sol"), "fork counts its inherited context as tokens processed by the child rollout");
File.WriteAllText(Path.Combine(forksRoot, "independent.jsonl"), """
{"type":"session_meta","payload":{"session_id":"s2","id":"s2","thread_source":"root"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-luna"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":40,"cached_input_tokens":0,"output_tokens":0,"total_tokens":40}}}}
""");
var withIndependent = new LocalUsageAnalyticsService().Read(5.5m, forksRoot);
Assert(withIndependent.MonthTokens == 430, "independent root still counts its first snapshot");
File.WriteAllText(Path.Combine(forksRoot, "spark.jsonl"), """
{"type":"session_meta","payload":{"session_id":"s3","id":"s3"}}
{"type":"turn_context","timestamp":"2026-08-12T10:00:00Z","payload":{"model":"gpt-5.3-codex-spark"}}
{"type":"event_msg","timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":10,"cached_input_tokens":0,"output_tokens":0,"total_tokens":10}}}}
""");
var spark = new LocalUsageAnalyticsService().Read(5.5m, forksRoot);
Assert(spark.Models.Any(x => x.Model == "gpt-5.3-codex-spark" && !x.Priced), "spark ID without official exact price remains unpriced");
Directory.Delete(forksRoot, true);
var restartRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-restart-" + Guid.NewGuid());
Directory.CreateDirectory(restartRoot);
var restartPath = Path.Combine(restartRoot, "restart.jsonl");
File.WriteAllText(restartPath, """
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-terra"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"total_tokens":100}}}}
{"timestamp":"2026-08-12T10:01:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":160,"total_tokens":160}}}}
{"timestamp":"2026-08-12T10:02:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":10,"total_tokens":10}}}}
{"timestamp":"2026-08-12T10:03:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":45,"total_tokens":45}}}}
""");
Assert(new LocalUsageAnalyticsService().Read(5.5m, restartRoot).MonthTokens == 205, "counter reset starts a new cumulative segment and counts its current snapshot");
var appendedForkPath = Path.Combine(restartRoot, "fork-append.jsonl");
File.WriteAllText(appendedForkPath, """{"type":"session_meta","payload":{"session_id":"r1","id":"f1","forked_from_id":"r1","thread_source":"subagent"}}""" + Environment.NewLine);
var appendForkAnalytics = new LocalUsageAnalyticsService();
Assert(appendForkAnalytics.Read(5.5m, restartRoot).MonthTokens == 205, "empty fork does not change usage");
File.AppendAllText(appendedForkPath, """{"timestamp":"2026-08-12T10:04:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":45,"total_tokens":45}}}}""" + Environment.NewLine);
Assert(appendForkAnalytics.Read(5.5m, restartRoot).MonthTokens == 250, "first fork snapshot received by append is processed context");
File.AppendAllText(appendedForkPath, """{"timestamp":"2026-08-12T10:05:00Z","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":70,"total_tokens":70}}}}""" + Environment.NewLine);
Assert(appendForkAnalytics.Read(5.5m, restartRoot).MonthTokens == 275, "fork append adds only the subsequent cumulative delta");
Directory.Delete(restartRoot, true);
var duplicateRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-duplicate-rollouts-" + Guid.NewGuid());
Directory.CreateDirectory(duplicateRoot);
var duplicateEarly = Path.Combine(duplicateRoot, "rollout-early.jsonl");
var duplicateFull = Path.Combine(duplicateRoot, "rollout-full.jsonl");
File.WriteAllText(duplicateEarly, """
{"type":"session_meta","payload":{"session_id":"logical-root","id":"logical-root","thread_source":"user"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-terra"}}
{"timestamp":"2026-08-12T10:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"total_tokens":100}}}}
{"timestamp":"2026-08-12T10:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":150,"total_tokens":150}}}}
""");
File.WriteAllText(duplicateFull, File.ReadAllText(duplicateEarly) + Environment.NewLine + """
{"timestamp":"2026-08-12T10:02:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":200,"total_tokens":200}}}}
{"timestamp":"2026-08-12T10:03:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":10,"total_tokens":10}}}}
{"timestamp":"2026-08-12T10:04:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":60,"total_tokens":60}}}}
""");
var duplicateAnalytics = new LocalUsageAnalyticsService();
var duplicateUsage = duplicateAnalytics.Read(5.5m, duplicateRoot);
Assert(duplicateUsage.MonthTokens == 260, "a prefixed checkpoint counts both cumulative segments while its shorter physical copy is ignored");
Assert(duplicateAnalytics.LogicalStreamsLastRead == 1 && duplicateAnalytics.DuplicatePhysicalFilesIgnoredLastRead == 1, "duplicate rollout diagnostics distinguish physical files from one logical stream");
File.AppendAllText(duplicateEarly, Environment.NewLine + """{"timestamp":"2026-08-12T10:03:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":250,"total_tokens":250}}}}""" + Environment.NewLine);
Assert(duplicateAnalytics.Read(5.5m, duplicateRoot).MonthTokens == 510, "a later divergent segment is retained instead of being discarded solely because its metadata id matches");
Directory.Delete(duplicateRoot, true);
var divergentRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-divergent-rollouts-" + Guid.NewGuid());
Directory.CreateDirectory(divergentRoot);
File.WriteAllText(Path.Combine(divergentRoot, "segment-a.jsonl"), """
{"type":"session_meta","payload":{"session_id":"segment-session","id":"segment-id","thread_source":"root"}}
{"timestamp":"2026-08-12T10:00:00Z","payload":{"type":"turn_context","model":"gpt-5.6-terra"}}
{"timestamp":"2026-08-12T10:00:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":100,"total_tokens":100}}}}
""");
File.WriteAllText(Path.Combine(divergentRoot, "segment-b.jsonl"), """
{"type":"session_meta","payload":{"session_id":"segment-session","id":"segment-id","thread_source":"root"}}
{"timestamp":"2026-08-12T10:01:00Z","payload":{"type":"turn_context","model":"gpt-5.6-sol"}}
{"timestamp":"2026-08-12T10:01:00Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":70,"total_tokens":70}}}}
""");
var divergentAnalytics = new LocalUsageAnalyticsService();
Assert(divergentAnalytics.Read(5.5m, divergentRoot).MonthTokens == 170, "same metadata id without a physical prefix remains independent evidence rather than being discarded");
Assert(divergentAnalytics.DuplicatePhysicalFilesIgnoredLastRead == 0, "only proven physical checkpoint prefixes are deduplicated");
Directory.Delete(divergentRoot, true);
var activityRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-agent-activity-" + Guid.NewGuid());
Directory.CreateDirectory(activityRoot);
var activityNow = new DateTimeOffset(2026, 8, 14, 13, 30, 0, TimeSpan.Zero);
var rootActivityPath = Path.Combine(activityRoot, "root.jsonl");
File.WriteAllText(rootActivityPath, """
{"timestamp":"2026-08-14T13:20:00Z","type":"session_meta","payload":{"session_id":"root-1","id":"root-1","thread_source":"user"}}
{"timestamp":"2026-08-14T13:20:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-root"}}
{"timestamp":"2026-08-14T13:20:02Z","type":"turn_context","payload":{"turn_id":"turn-root","model":"gpt-5.6-terra","effort":"medium"}}
{"timestamp":"2026-08-14T13:29:45Z","type":"event_msg","payload":{"type":"agent_reasoning","text":"  **Validando o build e os testes\ncom atencao aos detalhes"}}
{"timestamp":"2026-08-14T13:29:47Z","type":"event_msg","payload":{"type":"agent_reasoning","text":"   "}}
{"timestamp":"2026-08-14T13:29:50Z","type":"event_msg","payload":{"type":"agent_message","phase":"commentary","message":"Validando o build\ncom detalhes"}}
{"timestamp":"2026-08-14T13:29:52Z","type":"response_item","payload":{"type":"message","role":"assistant","phase":"commentary","text":"Output posterior diferente"}}
""");
var subagentActivityPath = Path.Combine(activityRoot, "subagent.jsonl");
File.WriteAllText(subagentActivityPath, """
{"timestamp":"2026-08-14T13:26:00Z","type":"session_meta","payload":{"session_id":"root-1","id":"sub-1","parent_thread_id":"root-1","thread_source":"subagent","agent_path":"/root/ui_review"}}
{"timestamp":"2026-08-14T13:26:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-sub"}}
{"timestamp":"2026-08-14T13:26:02Z","type":"turn_context","payload":{"turn_id":"turn-sub","model":"gpt-5.6-luna","effort":"low"}}
{"timestamp":"2026-08-14T13:29:55Z","type":"event_msg","payload":{"type":"token_count"}}
""");
var grandchildActivityPath = Path.Combine(activityRoot, "grandchild.jsonl");
File.WriteAllText(grandchildActivityPath, """
{"timestamp":"2026-08-14T13:22:00Z","type":"session_meta","payload":{"session_id":"root-1","id":"grand-1","parent_thread_id":"sub-1","thread_source":"subagent","agent_path":"/root/ui_review/check"}}
{"timestamp":"2026-08-14T13:22:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-grand"}}
{"timestamp":"2026-08-14T13:29:54Z","type":"event_msg","payload":{"type":"token_count"}}
""");
var rootBActivityPath = Path.Combine(activityRoot, "root-b.jsonl");
File.WriteAllText(rootBActivityPath, """
{"timestamp":"2026-08-14T13:23:00Z","type":"session_meta","payload":{"session_id":"root-b","id":"root-b","thread_source":"user"}}
{"timestamp":"2026-08-14T13:23:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-root-b"}}
{"timestamp":"2026-08-14T13:29:53Z","type":"event_msg","payload":{"type":"token_count"}}
""");
var orphanActivityPath = Path.Combine(activityRoot, "orphan.jsonl");
File.WriteAllText(orphanActivityPath, """
{"timestamp":"2026-08-14T13:24:00Z","type":"session_meta","payload":{"session_id":"orphan","id":"orphan","parent_thread_id":"missing-parent","thread_source":"subagent"}}
{"timestamp":"2026-08-14T13:24:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-orphan"}}
{"timestamp":"2026-08-14T13:29:52Z","type":"event_msg","payload":{"type":"token_count"}}
""");
var cycleAActivityPath = Path.Combine(activityRoot, "cycle-a.jsonl");
File.WriteAllText(cycleAActivityPath, """
{"timestamp":"2026-08-14T13:25:00Z","type":"session_meta","payload":{"session_id":"cycle-a","id":"cycle-a","parent_thread_id":"cycle-b","thread_source":"subagent"}}
{"timestamp":"2026-08-14T13:25:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-cycle-a"}}
""");
var cycleBActivityPath = Path.Combine(activityRoot, "cycle-b.jsonl");
File.WriteAllText(cycleBActivityPath, """
{"timestamp":"2026-08-14T13:26:00Z","type":"session_meta","payload":{"session_id":"cycle-b","id":"cycle-b","parent_thread_id":"cycle-a","thread_source":"subagent"}}
{"timestamp":"2026-08-14T13:26:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"turn-cycle-b"}}
""");
File.SetLastWriteTimeUtc(rootActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(subagentActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(grandchildActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(rootBActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(orphanActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(cycleAActivityPath, activityNow.UtcDateTime);
File.SetLastWriteTimeUtc(cycleBActivityPath, activityNow.UtcDateTime);
var activityService = new AgentActivityService(() => activityNow);
var activeAgents = activityService.Read(new Dictionary<string, string> { ["root-1"] = "Indicador de agentes" }, activityRoot);
Assert(activeAgents.Count == 7, "active task markers expose roots, descendants, orphans and cycles exactly once");
Assert(activeAgents.Select(agent => agent.ThreadId).SequenceEqual(["root-1", "sub-1", "grand-1", "root-b", "orphan", "cycle-a", "cycle-b"]), "active agents are depth-first by stable roots and descendants, with cycle fallback order");
var activeRoot = activeAgents.Single(x => x.ThreadId == "root-1");
Assert(activeRoot.Type == "Agent" && activeRoot.HierarchyDepth == 0 && activeRoot.Title == "Indicador de agentes" && activeRoot.Status == "Validando o build e os testes" && activeRoot.Model == "gpt-5.6-terra" && activeRoot.Effort == "medium", "principal activity preserves title, active reasoning, model and effort even after later commentary");
var activeSubagent = activeAgents.Single(x => x.ThreadId == "sub-1");
Assert(activeSubagent.Type == "Subagent" && activeSubagent.HierarchyDepth == 1 && activeSubagent.Title == "ui review" && activeSubagent.ParentThreadId == "root-1" && activeSubagent.Model == "gpt-5.6-luna" && activeSubagent.Effort == "low", "subagent activity uses its own id, depth and safe path fallback title");
Assert(activeAgents.Single(x => x.ThreadId == "grand-1").HierarchyDepth == 2 && activeAgents.Single(x => x.ThreadId == "root-b").HierarchyDepth == 0 && activeAgents.Single(x => x.ThreadId == "orphan").HierarchyDepth == 0, "grandchildren nest while roots with missing parents remain visual roots");
File.AppendAllText(rootActivityPath, "\n{\"timestamp\":\"2026-08-14T13:29:58Z\",\"type\":\"event_msg\",\"payload\":");
File.SetLastWriteTimeUtc(rootActivityPath, activityNow.UtcDateTime.AddSeconds(1));
Assert(activityService.Read(null, activityRoot).Any(x => x.ThreadId == "root-1"), "partial active JSONL line does not erase the committed running state");
File.AppendAllText(rootActivityPath, "{\"type\":\"task_complete\",\"turn_id\":\"turn-root\"}}\n");
File.SetLastWriteTimeUtc(rootActivityPath, activityNow.UtcDateTime.AddSeconds(2));
Assert(activityService.Read(null, activityRoot).All(x => x.ThreadId != "root-1"), "matching task_complete removes the principal from the running list after a partial append completes");
var staleActivityPath = Path.Combine(activityRoot, "stale.jsonl");
File.WriteAllText(staleActivityPath, """
{"timestamp":"2026-08-14T12:00:00Z","type":"session_meta","payload":{"session_id":"stale","id":"stale","thread_source":"user"}}
{"timestamp":"2026-08-14T12:00:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"stale-turn"}}
""");
File.SetLastWriteTimeUtc(staleActivityPath, activityNow.AddMinutes(-10).UtcDateTime);
Assert(activityService.Read(null, activityRoot).All(x => x.ThreadId != "stale"), "abandoned task_started files age out instead of remaining falsely active");
Directory.Delete(activityRoot, true);
Console.WriteLine("All CodexTracker core tests passed.");

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Test failed: " + message); }
static bool NearlyEqual(double? actual, double expected) => actual is double value && Math.Abs(value - expected) < 0.000001;
static bool NearlyEqualInstant(DateTimeOffset? actual, DateTimeOffset expected) => actual is { } value && Math.Abs((value - expected).TotalMilliseconds) < 1;
