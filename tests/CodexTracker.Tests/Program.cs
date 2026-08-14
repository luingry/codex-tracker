using System.IO;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using CodexTracker.Core;
using CodexTracker;

if (args.Contains("--benchmark-analytics", StringComparer.OrdinalIgnoreCase))
{
    var benchmarkRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-benchmark-" + Guid.NewGuid());
    Directory.CreateDirectory(benchmarkRoot);
    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var sourceRoots = new[] { Path.Combine(userProfile, ".codex", "sessions"), Path.Combine(userProfile, ".codex", "archived_sessions") };
    var copiedFiles = 0;
    foreach (var sourceRoot in sourceRoots.Where(Directory.Exists))
    {
        foreach (var sourcePath in Directory.EnumerateFiles(sourceRoot, "*.jsonl", SearchOption.AllDirectories))
        {
            CopyFileSnapshot(sourcePath, Path.Combine(benchmarkRoot, $"{copiedFiles++:D5}-{Path.GetFileName(sourcePath)}"));
        }
    }
    var samples = new Dictionary<int, List<long>> { [1] = [], [2] = [] };
    UsageAnalytics? reference = null;
    foreach (var degree in new[] { 1, 2, 2, 1, 1, 2 })
    {
        var service = new LocalUsageAnalyticsService(maxParseParallelism: degree);
        var stopwatch = Stopwatch.StartNew();
        var usage = service.Read(5.5m, benchmarkRoot);
        var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        samples[degree].Add(elapsedMilliseconds);
        if (reference is null) reference = usage;
        else if (usage.MonthTokens != reference.MonthTokens || usage.TodayTokens != reference.TodayTokens || usage.MonthUsd != reference.MonthUsd || usage.TodayUsd != reference.TodayUsd)
            throw new InvalidOperationException("Sequential and parallel analytics returned different totals during the benchmark.");
        Console.WriteLine($"cold-service degree={degree} sample={samples[degree].Count} ms={elapsedMilliseconds} files={service.FilesParsedLastRead} bytes={service.BytesReadLastRead} monthTokens={usage.MonthTokens} todayTokens={usage.TodayTokens}");
    }
    foreach (var degree in new[] { 1, 2 })
    {
        samples[degree].Sort();
        Console.WriteLine($"cold-service degree={degree} medianMs={samples[degree][samples[degree].Count / 2]} minMs={samples[degree][0]} maxMs={samples[degree][samples[degree].Count - 1]}");
    }
    Directory.Delete(benchmarkRoot, true);
    return;
}

var mainWindowSource = File.ReadAllText(FindRepositoryFile("src", "CodexTracker", "MainWindow.xaml.cs"));
var themeManagerSource = File.ReadAllText(FindRepositoryFile("src", "CodexTracker", "ThemeManager.cs"));
var loadedHandlerStart = mainWindowSource.IndexOf("Loaded += (_, _) =>", StringComparison.Ordinal);
var loadedHandlerEnd = mainWindowSource.IndexOf("Closing += OnClosing;", loadedHandlerStart, StringComparison.Ordinal);
var loadedHandler = mainWindowSource.Substring(loadedHandlerStart, loadedHandlerEnd - loadedHandlerStart);
Assert(loadedHandler.Contains("_ = LoadAsync();", StringComparison.Ordinal) &&
       loadedHandler.Contains("_ = RefreshAgentsAsync();", StringComparison.Ordinal) &&
       loadedHandler.Contains("_ = RefreshAnalyticsAsync();", StringComparison.Ordinal) &&
       !loadedHandler.Contains("if (_viewModel.Expanded) _ = RefreshAnalyticsAsync();", StringComparison.Ordinal),
       "startup keeps quota, agents and local analytics independent and parallel even when the widget begins compact");
var snapshotHandlerStart = mainWindowSource.IndexOf("Action<RateLimitSnapshot> snapshotUpdated = snapshot =>", StringComparison.Ordinal);
var snapshotHandlerEnd = mainWindowSource.IndexOf("await client.StartAsync", snapshotHandlerStart, StringComparison.Ordinal);
var snapshotHandler = mainWindowSource.Substring(snapshotHandlerStart, snapshotHandlerEnd - snapshotHandlerStart);
Assert(snapshotHandler.IndexOf("_viewModel.ApplyQuota(snapshot)", StringComparison.Ordinal) >= 0 &&
       snapshotHandler.IndexOf("_startupAnalytics.OnSnapshot", StringComparison.Ordinal) >= 0 &&
       mainWindowSource.Contains("_startupAnalytics.OnAnalyticsReady", StringComparison.Ordinal),
       "the startup race joins quota and analytics without discarding whichever finishes first");
Assert(mainWindowSource.Contains("await Task.Run(", StringComparison.Ordinal) &&
       mainWindowSource.Contains("() => CodexExecutableDiscovery.Find(_settings.CodexPath)", StringComparison.Ordinal) &&
       mainWindowSource.Contains("_shutdown.Token);", StringComparison.Ordinal),
       "Codex executable discovery leaves the UI dispatcher with shutdown cancellation before app-server startup");
var discoveryAwait = mainWindowSource.IndexOf("() => CodexExecutableDiscovery.Find(_settings.CodexPath)", StringComparison.Ordinal);
var startupReset = mainWindowSource.LastIndexOf("_startupAnalytics.BeginConnection();", discoveryAwait, StringComparison.Ordinal);
var cancellationCheck = mainWindowSource.IndexOf("_shutdown.Token.ThrowIfCancellationRequested();", discoveryAwait, StringComparison.Ordinal);
var clientCreation = mainWindowSource.IndexOf("var client = new CodexAppServerClient(executable);", cancellationCheck, StringComparison.Ordinal);
Assert(startupReset >= 0 && startupReset < discoveryAwait && cancellationCheck > discoveryAwait && clientCreation > cancellationCheck,
       "startup coordination resets before asynchronous discovery, then shutdown is rechecked before app-server client creation");
Assert(mainWindowSource.Contains("client.StatusChanged -= statusChanged;", StringComparison.Ordinal) &&
       mainWindowSource.Contains("client.SnapshotUpdated -= snapshotUpdated;", StringComparison.Ordinal) &&
       mainWindowSource.Contains("if (ReferenceEquals(_client, client)) _client = null;", StringComparison.Ordinal) &&
       mainWindowSource.Contains("await client.DisposeAsync();", StringComparison.Ordinal),
       "a failed app-server start detaches callbacks, clears ownership and disposes the newly created client");
Assert(mainWindowSource.Contains("if (Interlocked.Increment(ref _refreshTick) % 5 == 0) _ = RefreshAnalyticsAsync();", StringComparison.Ordinal) && !mainWindowSource.Contains("if (_viewModel.Expanded && Interlocked.Increment(ref _refreshTick)", StringComparison.Ordinal), "the five-minute analytics refresh continues in compact mode while retaining its non-overlapping scheduling gate");
Assert(mainWindowSource.Contains("private UsageAnalytics? _lastLoadedAnalytics;", StringComparison.Ordinal) && mainWindowSource.Contains("_lastLoadedAnalytics = usage;", StringComparison.Ordinal) && mainWindowSource.Contains("private void ApplyCachedAnalyticsIfDetailed(RateLimitSnapshot snapshot)", StringComparison.Ordinal) && mainWindowSource.Contains("if (!_viewModel.Expanded || _lastLoadedAnalytics is not { } usage || usage.UsdBrl != _settings.UsdBrl) return;", StringComparison.Ordinal) && mainWindowSource.Contains("if (_client?.Snapshot is { } snapshot) ApplyCachedAnalyticsIfDetailed(snapshot);", StringComparison.Ordinal), "compact analytics stay cached without touching the compact UI, apply immediately only when their USD/BRL rate matches current settings, and otherwise wait for the scheduled fresh read");

var coordinatorNow = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
var coordinatorSnapshot = new RateLimitSnapshot([new("codex:primary", "Weekly", 20, coordinatorNow.AddDays(4), 10080)], null, null, null, coordinatorNow);
var coordinatorUsage = new UsageAnalytics(123, 456, 1.2m, 6.6m, 100, []);
var analyticsFirstCoordinator = new StartupAnalyticsCoordinator();
var analyticsFirstGeneration = analyticsFirstCoordinator.BeginConnection();
Assert(analyticsFirstCoordinator.OnAnalyticsReady(coordinatorUsage, true) is null, "analytics first waits for the startup quota snapshot without being discarded");
Assert(analyticsFirstCoordinator.OnSnapshot(coordinatorSnapshot, true, analyticsFirstGeneration) == coordinatorUsage && analyticsFirstCoordinator.OnSnapshot(coordinatorSnapshot, true, analyticsFirstGeneration) is null, "quota first consumes an analytics-first result exactly once");
var snapshotFirstCoordinator = new StartupAnalyticsCoordinator();
var snapshotFirstGeneration = snapshotFirstCoordinator.BeginConnection();
Assert(snapshotFirstCoordinator.OnSnapshot(coordinatorSnapshot, true, snapshotFirstGeneration) is null, "quota first has no pending analytics result to consume");
var snapshotFirstApplication = snapshotFirstCoordinator.OnAnalyticsReady(coordinatorUsage, true);
Assert(snapshotFirstApplication is { } && snapshotFirstApplication.Value.Snapshot == coordinatorSnapshot && snapshotFirstApplication.Value.Usage == coordinatorUsage, "analytics first after a quota snapshot applies immediately against that snapshot");
var reconnectPendingCoordinator = new StartupAnalyticsCoordinator();
var staleGeneration = reconnectPendingCoordinator.BeginConnection();
Assert(reconnectPendingCoordinator.OnAnalyticsReady(coordinatorUsage, true) is null, "analytics-first result is pending before reconnect");
var currentGeneration = reconnectPendingCoordinator.BeginConnection();
Assert(!reconnectPendingCoordinator.IsCurrent(staleGeneration) && reconnectPendingCoordinator.OnSnapshot(coordinatorSnapshot, true, staleGeneration) is null, "a callback from the previous app-server generation cannot repopulate the coordinator during reconnect");
Assert(reconnectPendingCoordinator.OnSnapshot(coordinatorSnapshot, true, currentGeneration) == coordinatorUsage && reconnectPendingCoordinator.OnSnapshot(coordinatorSnapshot, true, currentGeneration) is null, "reconnect preserves pending local analytics until the current snapshot consumes it once");
var reconnectSnapshotCoordinator = new StartupAnalyticsCoordinator();
var previousSnapshotGeneration = reconnectSnapshotCoordinator.BeginConnection();
_ = reconnectSnapshotCoordinator.OnSnapshot(coordinatorSnapshot, true, previousSnapshotGeneration);
var nextSnapshotGeneration = reconnectSnapshotCoordinator.BeginConnection();
Assert(reconnectSnapshotCoordinator.OnAnalyticsReady(coordinatorUsage, true) is null, "analytics after reconnect never applies against the old snapshot");
Assert(reconnectSnapshotCoordinator.OnSnapshot(coordinatorSnapshot, true, nextSnapshotGeneration) == coordinatorUsage, "analytics after reconnect waits for and applies against the new snapshot");
Assert(mainWindowSource.Contains("_startupAnalytics.BeginConnection();", StringComparison.Ordinal) && mainWindowSource.Contains("_startupAnalytics.IsCurrent(connectionGeneration)", StringComparison.Ordinal), "new app-server clients invalidate previous callbacks before asynchronous discovery and recheck generation on the dispatcher");

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
Assert(AccentPalette.DarkSurfaceHex == "#2D2D2D" && AccentPalette.ContrastRatio(lightAccent.AccentHex, "#F7F7F4") >= 4.5 && AccentPalette.ContrastRatio(darkAccent.AccentHex, AccentPalette.DarkSurfaceHex) >= 4.5, "derived accent text remains readable on light and the exact #2D2D2D dark primary surface");
Assert(lightAccent.SoftHex != lightAccent.AccentHex && lightAccent.HoverHex != lightAccent.AccentHex && lightAccent.GlowHex != lightAccent.AccentHex, "a single accent seed derives distinct soft, hover and glow tonal roles");
Assert(AccentPalette.ContrastRatio(lightAccent.AgentMetadataHex, "#F7F7F4") >= 4.5 && AccentPalette.ContrastRatio(darkAccent.AgentMetadataHex, AccentPalette.DarkSurfaceHex) >= 4.5 && AccentPalette.Saturation(lightAccent.AgentMetadataHex) < AccentPalette.Saturation(lightAccent.AccentHex) && AccentPalette.Saturation(darkAccent.AgentMetadataHex) < AccentPalette.Saturation(darkAccent.AccentHex), "agent model and effort receive a less saturated but contrast-safe accent role in both themes");
Assert(themeManagerSource.Contains("Set(\"Porcelain\", dark ? AccentPalette.DarkSurfaceHex", StringComparison.Ordinal) && themeManagerSource.Contains("Set(\"DetailedSurface\", dark ? \"#FF2D2D2D\"", StringComparison.Ordinal) && themeManagerSource.Contains("Set(\"GlassSurface\", dark ? \"#FF2D2D2D\"", StringComparison.Ordinal) && themeManagerSource.Contains("Set(\"SettingsSurface\", dark ? \"#FF2D2D2D\"", StringComparison.Ordinal), "dark primary, detailed, glass and settings surfaces consistently use the requested #2D2D2D base");
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
var rankingAnalytics = new UsageAnalytics(0, 143, 0, 0, 0, [new ModelUsage("month-model", 99, 9.9m, true), new ModelUsage("no-tariff-model", 44, 0, false), new ModelUsage("unknown", 33, 0, false), new ModelUsage("unknown-model", 22, 0, false)], UsdBrl: 5m, ModelTimeline:
[
    new(rankingNow.AddMinutes(-1), "day-model", 11, 1.25m, true),
    new(rankingCycleStart.AddMinutes(1), "cycle-model", 50, 2.5m, true),
    new(rankingCycleStart.AddMinutes(-1), "outside-cycle-model", 99, 9.9m, true)
]);
var rankingViewModel = new MainViewModel();
rankingViewModel.Apply(new RateLimitSnapshot([new("codex:primary", "Weekly limit", 16, rankingCycleEnd, 10080)], null, null, null, rankingNow), rankingAnalytics);
Assert(rankingViewModel.Ranking.First().Model == "month-model" && rankingViewModel.Ranking.First().SecondaryText == "R$ 49,50" && rankingViewModel.Ranking.Single(row => row.Model == "no-tariff-model").SecondaryText == "sem tarifa", "monthly ranking shows the priced model cost converted with analytics USD/BRL and preserves the localized no-tariff state for unpriced models in the same secondary line");
Assert(rankingViewModel.Ranking.All(row => row.Model != "unknown") && rankingViewModel.Ranking.Single(row => row.Model == "Modelo não registrado").Tokens == "33" && rankingViewModel.Ranking.Single(row => row.Model == "unknown-model").Tokens == "22", "ranking localizes only the internal unknown bucket without hiding tokens or renaming literal model names");
rankingViewModel.SetCurrency("USD");
Assert(rankingViewModel.Ranking.First().SecondaryText == "US$ 9,90", "monthly ranking refreshes priced costs when the selected currency changes to USD");
LocalizationManager.Apply("en-US");
rankingViewModel.RefreshLocalization();
Assert(rankingViewModel.Ranking.First().SecondaryText == "US$ 9.90" && rankingViewModel.Ranking.Single(row => row.Model == "no-tariff-model").SecondaryText == "no tariff" && rankingViewModel.Ranking.Single(row => row.Model == "Model not recorded").Tokens == "33" && rankingViewModel.Ranking.Any(row => row.Model == "unknown-model"), "ranking refreshes cost formatting, unpriced text and the internal unknown label when the interface language changes to en-US without renaming literal model names");
rankingViewModel.SetCurrency("BRL");
Assert(rankingViewModel.Ranking.First().SecondaryText == "R$ 49.50", "ranking refreshes BRL cost formatting using the active en-US culture");
rankingViewModel.IsRankingDay = true;
Assert(rankingViewModel.Ranking.Single().Model == "day-model" && rankingViewModel.Ranking.Single().SecondaryText == "R$ 6.25", "day ranking filters model usage and uses the day CostUsd rather than the monthly aggregate");
rankingViewModel.IsRankingWeek = true;
Assert(rankingViewModel.Ranking.Any(row => row.Model == "cycle-model" && row.SecondaryText == "R$ 12.50") && rankingViewModel.Ranking.All(row => row.Model != "outside-cycle-model"), "week ranking uses only the active Codex quota cycle and its period CostUsd, excluding usage outside the cycle");
LocalizationManager.Apply("pt-BR");
rankingViewModel.RefreshLocalization();
rankingViewModel.ApplyQuota(new RateLimitSnapshot([], null, null, null, rankingNow));
Assert(rankingViewModel.Ranking.Count == 0, "week ranking is empty when the official active Codex quota cycle is unavailable");

var compactSize = WidgetSizePolicy.Normalize(WidgetVisualMode.Compact, new WidgetSize(999, 1));
Assert(compactSize.Width == 100 && Math.Abs(compactSize.Height * 62 - compactSize.Width * 52) < 0.001, "compact size clamps width at 100 and preserves the 62:52 ratio");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(1, 999)) == new WidgetSize(300, 720), "detailed size keeps fixed width and clamps visible height");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 300)) == new WidgetSize(300, 300) && WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 1)) == new WidgetSize(300, 260), "detailed preserves resized heights down to 260 and clamps below it");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Settings, new WidgetSize(double.NaN, 0)) == WidgetSizePolicy.Default(WidgetVisualMode.Settings), "invalid settings size falls back to its safe default");
Assert(WidgetSizePolicy.SettingsHeightForContent(612.2, 1000) == 613 && WidgetSizePolicy.SettingsHeightForContent(1000, 600) == 600 && WidgetSizePolicy.SettingsHeightForContent(100, 300) == 300, "settings content height expands to the required whole DIP while respecting the work-area and policy cap");
Assert(WidgetSizePolicy.DetailedMaxHeightForContent(512.2) == 513 && WidgetSizePolicy.DetailedMaxHeightForContent(999) == WidgetSizePolicy.DetailedMaxHeight, "detailed maximum height follows rounded content height without exceeding the safety cap");
Assert(WidgetSizePolicy.DetailedHeightForContent(512.2, 1000) == 513 && WidgetSizePolicy.DetailedHeightForContent(1000, 600) == 600 && WidgetSizePolicy.DetailedHeightForContent(100, 220) == 220, "detailed content height expands to the required whole DIP while respecting both the work-area and detailed safety cap");
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
Assert(indicatorTemplate.Contains("x:Name=\"AgentIndicatorSurface\" Background=\"#2D2D2D\"", StringComparison.Ordinal) && indicatorTemplate.Contains("<Border.Effect><DropShadowEffect BlurRadius=\"5\" ShadowDepth=\"1\" Opacity=\".30\" Color=\"#151A18\" /></Border.Effect>", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"CompactGaugeSurface\"", StringComparison.Ordinal) && mainWindowXaml.Contains("<Ellipse.Effect><DropShadowEffect BlurRadius=\"5\" ShadowDepth=\"1\" Opacity=\".30\" Color=\"#151A18\" /></Ellipse.Effect>", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"AgentIndicatorButton\" Width=\"20\" Height=\"20\" Padding=\"0\" Margin=\"0,-1,0,5\"", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"WindowSurface\" CornerRadius=\"12\" ClipToBounds=\"True\"", StringComparison.Ordinal) && mainWindowSource.Contains("private const double CompactAgentIndicatorHeight = 24d;", StringComparison.Ordinal), "compact gauge and dark agent indicator use coherent subtle shadows with explicit lower space while the rounded window keeps its intended clipping");
Assert(indicatorTemplate.Contains("x:Name=\"AgentIndicatorSurface\" Background=\"#2D2D2D\" CornerRadius=\"10\" ClipToBounds=\"True\"", StringComparison.Ordinal) && indicatorTemplate.Contains("<Ellipse x:Name=\"AgentWorkGlow\" Width=\"18\" Height=\"18\" Opacity=\".10\" Visibility=\"Collapsed\" IsHitTestVisible=\"False\"", StringComparison.Ordinal) && indicatorTemplate.Contains("<RadialGradientBrush Center=\"0.5,0.5\" GradientOrigin=\"0.5,0.5\" RadiusX=\"0.5\" RadiusY=\"0.5\">", StringComparison.Ordinal) && indicatorTemplate.Contains("<GradientStop x:Name=\"AgentWorkGlowInnerEdge\" Color=\"Transparent\" Offset=\".88\" />", StringComparison.Ordinal) && indicatorTemplate.Contains("<GradientStop Color=\"#FFFFFF\" Offset=\"1\" />", StringComparison.Ordinal) && indicatorTemplate.Contains("<BlurEffect Radius=\"1.5\" />", StringComparison.Ordinal) && indicatorTemplate.Contains("<DataTrigger Binding=\"{Binding IsWorkAnimationEnabled}\" Value=\"True\">", StringComparison.Ordinal) && indicatorTemplate.Contains("x:Name=\"AgentWorkGlowStoryboard\"", StringComparison.Ordinal) && indicatorTemplate.Contains("Storyboard.TargetName=\"AgentWorkGlowInnerEdge\" Storyboard.TargetProperty=\"Offset\" From=\".88\" To=\".58\" Duration=\"0:0:1.00\" AutoReverse=\"True\"", StringComparison.Ordinal) && indicatorTemplate.Contains("Storyboard.TargetName=\"AgentWorkGlow\" Storyboard.TargetProperty=\"Opacity\" From=\".10\" To=\".34\" Duration=\"0:0:1.00\" AutoReverse=\"True\"", StringComparison.Ordinal) && indicatorTemplate.Contains("<Storyboard RepeatBehavior=\"Forever\">", StringComparison.Ordinal) && !indicatorTemplate.Contains("AgentWorkSpinner", StringComparison.Ordinal) && !indicatorTemplate.Contains("M10,1 A9,9", StringComparison.Ordinal) && !indicatorTemplate.Contains("Fill=\"#FFFFFF\"", StringComparison.Ordinal) && !indicatorTemplate.Contains("ScaleTransform", StringComparison.Ordinal) && !indicatorTemplate.Contains("RotateTransform", StringComparison.Ordinal) && !indicatorTemplate.Contains("Storyboard.TargetProperty=\"Angle\"", StringComparison.Ordinal) && indicatorTemplate.Contains("<StopStoryboard BeginStoryboardName=\"AgentWorkGlowStoryboard\" />", StringComparison.Ordinal), "active agents display a clipped edge-anchored radial glow that expands inward through its transparent stop and opacity only while work animation is allowed and stops cleanly for reduced motion");
var agentListStart = mainWindowXaml.IndexOf("<Popup x:Name=\"AgentListPopup\"", StringComparison.Ordinal);
var agentListEnd = mainWindowXaml.IndexOf("</Popup>", agentListStart, StringComparison.Ordinal);
var agentListTemplate = agentListStart >= 0 && agentListEnd > agentListStart ? mainWindowXaml.Substring(agentListStart, agentListEnd - agentListStart) : string.Empty;
var roundedClipBorderSource = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "CodexTracker", "RoundedClipBorder.cs"));
Assert(agentListTemplate.Contains("x:Name=\"AgentListWrapper\" Width=\"288\" MaxHeight=\"350\" Background=\"Transparent\"", StringComparison.Ordinal) && agentListTemplate.Contains("<Border.Effect><DropShadowEffect BlurRadius=\"12\" ShadowDepth=\"3\" Opacity=\".28\" Color=\"#151A18\" /></Border.Effect>", StringComparison.Ordinal) && agentListTemplate.Contains("<local:RoundedClipBorder x:Name=\"AgentListClipSurface\" Padding=\"0\" CornerRadius=\"12\" Background=\"{DynamicResource DetailedSurface}\"", StringComparison.Ordinal) && agentListTemplate.Contains("x:Name=\"AgentRow\" Margin=\"0\"", StringComparison.Ordinal) && agentListTemplate.Contains("<ContentPresenter Margin=\"0,8\" HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\" />", StringComparison.Ordinal) && agentListTemplate.Contains("<Border Margin=\"{Binding Indent}\" Padding=\"15,6\">", StringComparison.Ordinal) && roundedClipBorderSource.Contains("Clip = new RectangleGeometry(new Rect(RenderSize), radius, radius);", StringComparison.Ordinal), "agent list keeps its shadow on an outer un-clipped wrapper while an inner dynamically sized rounded geometry clips every contiguous full-width row interaction");
Assert(agentListTemplate.Contains("Text=\"{Binding ModelAndEffort}\"", StringComparison.Ordinal) && agentListTemplate.Contains("Foreground=\"{DynamicResource AgentMetadataAccent}\"", StringComparison.Ordinal), "agent model and effort bind to the contrast-safe muted accent resource instead of fixed opacity or generic secondary ink");
Assert(agentListTemplate.Contains("<Grid.ColumnDefinitions><ColumnDefinition Width=\"Auto\" /><ColumnDefinition Width=\"*\" /></Grid.ColumnDefinitions>", StringComparison.Ordinal) && agentListTemplate.Contains("x:Name=\"KindLabel\" Text=\"{Binding Type}\" MaxWidth=\"58\"", StringComparison.Ordinal) && agentListTemplate.Contains("Margin=\"0,0,6,0\"", StringComparison.Ordinal) && agentListTemplate.Contains("Grid.Column=\"1\" Text=\"{Binding ModelAndEffort}\"", StringComparison.Ordinal) && agentListTemplate.Contains("TextTrimming=\"CharacterEllipsis\" HorizontalAlignment=\"Left\"", StringComparison.Ordinal) && agentListTemplate.Contains("Grid.Column=\"1\" Text=\"{Binding Elapsed}\"", StringComparison.Ordinal) && !agentListTemplate.Contains("TextAlignment=\"Right\"", StringComparison.Ordinal) && !agentListTemplate.Contains("Grid.Row=\"3\" Text=\"{Binding ModelAndEffort}\"", StringComparison.Ordinal), "agent model and effort stay immediately after a bounded type label with a consistent small gap and truncation, while elapsed remains on the status row");
Assert(mainWindowXaml.Contains("Text=\"{Binding Tokens}\" FontSize=\"10\"", StringComparison.Ordinal) && mainWindowXaml.Contains("Text=\"{Binding SecondaryText}\" FontSize=\"8\" Foreground=\"{DynamicResource SoftInk}\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Text=\"{Binding Cost}\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Text=\"{Binding TariffNote}\"", StringComparison.Ordinal), "ranking rows use exactly one small secondary line below tokens for either the estimated cost or the localized no-tariff text");
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
var comboBoxTemplateStart = appXaml.IndexOf("<Style TargetType=\"ComboBox\">", StringComparison.Ordinal);
var comboBoxTemplateEnd = appXaml.IndexOf("</Style>", comboBoxTemplateStart, StringComparison.Ordinal);
var comboBoxTemplate = comboBoxTemplateStart >= 0 && comboBoxTemplateEnd > comboBoxTemplateStart ? appXaml.Substring(comboBoxTemplateStart, comboBoxTemplateEnd - comboBoxTemplateStart) : string.Empty;
var checkBoxTemplateStart = appXaml.IndexOf("<Style TargetType=\"CheckBox\">", StringComparison.Ordinal);
var checkBoxTemplateEnd = appXaml.IndexOf("</Style>", checkBoxTemplateStart, StringComparison.Ordinal);
var checkBoxTemplate = checkBoxTemplateStart >= 0 && checkBoxTemplateEnd > checkBoxTemplateStart ? appXaml.Substring(checkBoxTemplateStart, checkBoxTemplateEnd - checkBoxTemplateStart) : string.Empty;
Assert(comboBoxTemplate.Contains("Padding=\"{TemplateBinding Padding}\" Focusable=\"False\"", StringComparison.Ordinal) && comboBoxTemplate.Contains("<Border x:Name=\"Surface\" Background=\"{TemplateBinding Background}\" CornerRadius=\"9\" Padding=\"{TemplateBinding Padding}\">", StringComparison.Ordinal) && appXaml.Split(new[] { "TextElement.Foreground=\"{TemplateBinding Foreground}\"" }, StringSplitOptions.None).Length >= 4 && appXaml.Contains("<Setter Property=\"Padding\" Value=\"14,6\" />", StringComparison.Ordinal) && appXaml.Contains("<Setter Property=\"Cursor\" Value=\"Hand\" />", StringComparison.Ordinal), "the ComboBox forwards its configured left padding into the inner toggle surface while selection and items retain foreground contrast and a hand cursor");
Assert(checkBoxTemplate.Contains("<Border x:Name=\"Mark\" Width=\"16\" Height=\"16\" CornerRadius=\"5\" Background=\"{DynamicResource InputSurface}\">", StringComparison.Ordinal) && !checkBoxTemplate.Contains("x:Name=\"Mark\" Width=\"16\" Height=\"16\" CornerRadius=\"5\" Background=\"{DynamicResource InputSurface}\" Margin=", StringComparison.Ordinal) && checkBoxTemplate.Contains("<ContentPresenter Margin=\"13,0,0,0\" VerticalAlignment=\"Center\" TextElement.Foreground=\"{TemplateBinding Foreground}\" />", StringComparison.Ordinal), "the standard checkbox keeps a stable 13-DIP gap after its marker without relying on BulletDecorator margin behavior");
Assert(appXaml.Contains("<Style x:Key=\"SettingsChoiceStyle\" TargetType=\"CheckBox\"", StringComparison.Ordinal) && appXaml.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource SoftInk}\" />", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"DetailedBox\" Content=\"{DynamicResource Loc.DetailedMode}\" Style=\"{StaticResource SettingsChoiceStyle}\" Foreground=\"{DynamicResource SoftInk}\"", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"TopmostBox\" Content=\"{DynamicResource Loc.AlwaysOnTop}\" Style=\"{StaticResource SettingsChoiceStyle}\" Foreground=\"{DynamicResource SoftInk}\"", StringComparison.Ordinal), "settings checkbox choices share the semantic secondary foreground explicitly without affecting ThemeSwitch");
Assert(mainWindowSource.Contains("if (SettingsPanel.Visibility == Visibility.Visible) return ResizeEdge.None;", StringComparison.Ordinal) && mainWindowSource.Contains("if (SettingsPanel.Visibility == Visibility.Visible) return;", StringComparison.Ordinal) && mainWindowSource.Contains("ScheduleSettingsHeightForContent();", StringComparison.Ordinal) && mainWindowSource.Contains("WidgetSizePolicy.SettingsHeightForContent(SettingsPanel.DesiredSize.Height, workArea.Height)", StringComparison.Ordinal), "settings disables manual resize and fits its required content height within the monitor work area");
Assert(mainWindowSource.Contains("WidgetSizePolicy.DetailedHeightForContent(", StringComparison.Ordinal) && mainWindowSource.Contains("if (Math.Abs(contentMaxHeight - _detailedContentMaxHeight) < .5d) return;", StringComparison.Ordinal) && mainWindowSource.Contains("Height = contentMaxHeight;", StringComparison.Ordinal) && mainWindowSource.Contains("Top = Math.Max(workArea.Top, Math.Min(Top, workArea.Top + workArea.Height - Height));", StringComparison.Ordinal), "detailed content applies its full permitted height only when its required size changes, preserving later manual resize while moving upward only when needed to stay inside the current monitor work area");
var progressStyleStart = appXaml.IndexOf("<Style TargetType=\"ProgressBar\">", StringComparison.Ordinal);
var progressStyleEnd = appXaml.IndexOf("</Style>", progressStyleStart, StringComparison.Ordinal);
var progressStyle = progressStyleStart >= 0 && progressStyleEnd > progressStyleStart ? appXaml.Substring(progressStyleStart, progressStyleEnd - progressStyleStart) : string.Empty;
Assert(!progressStyle.Contains("WorkGlow", StringComparison.Ordinal) && !progressStyle.Contains("IsWorkAnimationEnabled", StringComparison.Ordinal) && mainWindowXaml.Contains("IsWorking=\"{Binding IsWorkAnimationEnabled}\"", StringComparison.Ordinal), "ranking progress bars stay static while the weekly circular quota gauges retain their dedicated work glow");
var reasoningGlowStart = mainWindowXaml.IndexOf("x:Name=\"ReasoningGlow\"", StringComparison.Ordinal);
var reasoningGlowEnd = mainWindowXaml.IndexOf("</DataTemplate>", reasoningGlowStart, StringComparison.Ordinal);
var reasoningGlowTemplate = reasoningGlowStart >= 0 && reasoningGlowEnd > reasoningGlowStart ? mainWindowXaml.Substring(reasoningGlowStart, reasoningGlowEnd - reasoningGlowStart) : string.Empty;
Assert(reasoningGlowTemplate.Contains("<LinearGradientBrush MappingMode=\"Absolute\" StartPoint=\"0,0.5\" EndPoint=\"64,0.5\">", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<LinearGradientBrush.Transform><TranslateTransform x:Name=\"ReasoningGlowTransform\" X=\"-64\" /></LinearGradientBrush.Transform>", StringComparison.Ordinal) && !reasoningGlowTemplate.Contains("LinearGradientBrush.RelativeTransform", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:0\" Value=\"-64\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:1.30\" Value=\"268\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("KeyTime=\"0:0:3.30\" Value=\"268\"", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<DataTrigger Binding=\"{Binding IsWorkAnimationEnabled}\" Value=\"True\">", StringComparison.Ordinal) && reasoningGlowTemplate.Contains("<StopStoryboard BeginStoryboardName=\"ReasoningGlowStoryboard\" />", StringComparison.Ordinal), "reasoning glow uses a fixed 64-DIP absolute band, a 1.30-second left-to-right sweep, two-second hold, and the work-animation/reduced-motion trigger");
var mainWindowCode = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "src", "CodexTracker", "MainWindow.xaml.cs"));
Assert(!mainWindowXaml.Contains("Click=\"ToggleTopmost\"", StringComparison.Ordinal) && !mainWindowCode.Contains("private void ToggleTopmost", StringComparison.Ordinal) && mainWindowXaml.Contains("x:Name=\"TopmostBox\" Content=\"{DynamicResource Loc.AlwaysOnTop}\"", StringComparison.Ordinal) && mainWindowCode.Contains("Topmost = TopmostBox.IsChecked == true;", StringComparison.Ordinal), "the visible pin control and its dead handler are removed while the Settings topmost preference remains functional");
Assert(mainWindowXaml.Contains("x:Name=\"CodexPathFallbackPanel\" Visibility=\"Collapsed\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Click=\"AutoDetect\"", StringComparison.Ordinal) && !mainWindowXaml.Contains("Click=\"TestPath\"", StringComparison.Ordinal), "manual Codex path UI is a collapsed fallback and does not expose the obsolete auto-detect or test actions");
Assert(mainWindowCode.Contains("var automaticallyDetectedPath = CodexExecutableDiscovery.Find(null);", StringComparison.Ordinal) && mainWindowCode.Contains("CodexPathFallbackPanel.Visibility = automaticallyDetectedPath is null ? Visibility.Visible : Visibility.Collapsed;", StringComparison.Ordinal) && mainWindowCode.Contains("PathBox.Text = automaticallyDetectedPath ?? _settings.CodexPath ?? \"\";", StringComparison.Ordinal), "settings show the manual Codex path fallback only when automatic discovery fails and otherwise keep the detected or persisted path available");
Assert(mainWindowCode.Contains("var manualCodexPath = CodexPathFallbackPanel.Visibility == Visibility.Visible", StringComparison.Ordinal) && mainWindowCode.Contains("? string.IsNullOrWhiteSpace(PathBox.Text) ? null : PathBox.Text", StringComparison.Ordinal) && mainWindowCode.Contains(": _settings.CodexPath;", StringComparison.Ordinal), "applying settings clears or accepts PathBox only while the fallback is visible and preserves the stored Codex path while it is hidden");
Assert(mainWindowCode.Contains("CreateTrayIcon(_settings.AccentColor)", StringComparison.Ordinal) && mainWindowCode.Contains("ColorTranslator.FromHtml(AccentPalette.Normalize(accentColor))", StringComparison.Ordinal) && mainWindowCode.Contains("CreateTray();", StringComparison.Ordinal), "tray fallback and recreated localized menu follow the persisted accent instead of retaining a fixed green");
Assert(mainWindowCode.Contains("previousIcon?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("previousMenu?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("_trayMenu?.Dispose();", StringComparison.Ordinal) && mainWindowCode.Contains("_trayIcon?.Dispose();", StringComparison.Ordinal), "repeated language previews replace and deterministically dispose tray icon and menu native resources");
Assert(mainWindowCode.Contains("LocalizationManager.Apply(_settings.LanguageCode);", StringComparison.Ordinal) && mainWindowCode.Contains("LanguageCode = language", StringComparison.Ordinal) && mainWindowCode.Contains("private void PreviewLanguage", StringComparison.Ordinal), "language preview, cancel restoration and apply persistence are wired through the settings lifecycle");
var refreshAgentsStart = mainWindowCode.IndexOf("private async Task RefreshAgentsAsync", StringComparison.Ordinal);
var refreshAgentsEnd = mainWindowCode.IndexOf("private void ToggleAgentList", refreshAgentsStart, StringComparison.Ordinal);
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
var analyticsService = new LocalUsageAnalyticsService(() => analyticsNow);
var analytics = analyticsService.Read(5.5m, analyticsRoot);
var analyticsReferenceService = new LocalUsageAnalyticsService(() => analyticsNow, maxParseParallelism: 1);
var analyticsReference = analyticsReferenceService.Read(5.5m, analyticsRoot);
Assert(analytics.MonthTokens == 180, "cumulative token snapshots use deltas, never 430");
Assert(analytics.MonthTokens == analyticsReference.MonthTokens && analytics.TodayTokens == analyticsReference.TodayTokens && analytics.MonthUsd == analyticsReference.MonthUsd && analytics.TodayUsd == analyticsReference.TodayUsd && analyticsService.FilesParsedLastRead == analyticsReferenceService.FilesParsedLastRead && analyticsService.FilesRebuiltLastRead == analyticsReferenceService.FilesRebuiltLastRead && analyticsService.BytesReadLastRead == analyticsReferenceService.BytesReadLastRead && analyticsService.FilesParsedLastRead == 1 && analyticsService.FilesRebuiltLastRead == 1 && analyticsService.BytesReadLastRead > 0, "bounded-parallel and sequential cold analytics preserve identical results and counters");
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
var threadSettingsRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-thread-settings-" + Guid.NewGuid());
Directory.CreateDirectory(threadSettingsRoot);
File.WriteAllText(Path.Combine(threadSettingsRoot, "settings.jsonl"), """
{"timestamp":"2026-08-12T10:00:00Z","type":"event_msg","payload":{"type":"model_provider","model":"gpt-5.6-sol"}}
{"timestamp":"2026-08-12T10:00:01Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":10,"total_tokens":10}}}}
{"timestamp":"2026-08-12T10:00:02Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-sol"}}}
{"timestamp":"2026-08-12T10:00:03Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":30,"total_tokens":30}}}}
{"timestamp":"2026-08-12T10:00:04Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-terra"}}}
{"timestamp":"2026-08-12T10:00:05Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":45,"total_tokens":45}}}}
""");
var threadSettingsUsage = new LocalUsageAnalyticsService(() => analyticsNow).Read(5.5m, threadSettingsRoot);
Assert(threadSettingsUsage.Models.Any(x => x.Model == "unknown" && x.Tokens == 10) &&
       threadSettingsUsage.Models.Any(x => x.Model == "gpt-5.6-sol" && x.Tokens == 20) &&
       threadSettingsUsage.Models.Any(x => x.Model == "gpt-5.6-terra" && x.Tokens == 15),
       "thread settings apply model attribution only to subsequent snapshots while model_provider alone remains unknown");
Directory.Delete(threadSettingsRoot, true);
var sqliteFallbackRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-sqlite-fallback-" + Guid.NewGuid());
Directory.CreateDirectory(sqliteFallbackRoot);
var sqliteFallbackPath = Path.Combine(sqliteFallbackRoot, "fallback.jsonl");
File.WriteAllText(sqliteFallbackPath, """
{"timestamp":"2026-08-12T10:00:00Z","type":"session_meta","payload":{"session_id":"sqlite-session","id":"sqlite-thread"}}
{"timestamp":"2026-08-12T10:00:01Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":10,"total_tokens":10}}}}
{"timestamp":"2026-08-12T10:00:02Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-terra"}}}
{"timestamp":"2026-08-12T10:00:03Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":25,"total_tokens":25}}}}
""" + Environment.NewLine);
File.WriteAllText(Path.Combine(sqliteFallbackRoot, "unmapped.jsonl"), """
{"timestamp":"2026-08-12T10:00:00Z","type":"session_meta","payload":{"session_id":"unmapped-session","id":"unmapped-thread"}}
{"timestamp":"2026-08-12T10:00:01Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":7,"total_tokens":7}}}}
""" + Environment.NewLine);
var sqliteFallbackDb = Path.Combine(sqliteFallbackRoot, "state.sqlite");
using (var connection = new SqliteConnection($"Data Source={sqliteFallbackDb};Pooling=False"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode=WAL";
    command.ExecuteScalar();
    command.CommandText = "CREATE TABLE threads (id TEXT PRIMARY KEY, model TEXT, reasoning_effort TEXT); INSERT INTO threads (id, model, reasoning_effort) VALUES ('sqlite-thread', 'gpt-5.6-sol', 'medium');";
    command.ExecuteNonQuery();
}
var sqliteFallbackUsage = new LocalUsageAnalyticsService(() => analyticsNow, stateDatabasePath: sqliteFallbackDb).Read(5.5m, sqliteFallbackRoot);
Assert(sqliteFallbackUsage.Models.Any(x => x.Model == "gpt-5.6-sol" && x.Tokens == 10) && sqliteFallbackUsage.Models.Any(x => x.Model == "gpt-5.6-terra" && x.Tokens == 15) && sqliteFallbackUsage.Models.Any(x => x.Model == "unknown" && x.Tokens == 7), "sqlite thread model seeds attribution before the first snapshot, later rollout settings remain temporal overrides, and missing thread models remain unknown");
var sqliteFallbackService = new LocalUsageAnalyticsService(() => analyticsNow, stateDatabasePath: sqliteFallbackDb);
_ = sqliteFallbackService.Read(5.5m, sqliteFallbackRoot);
var sqliteFallbackWal = sqliteFallbackDb + "-wal";
using (var connection = new SqliteConnection($"Data Source={sqliteFallbackDb};Pooling=False"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "UPDATE threads SET model = 'gpt-5.6-luna' WHERE id = 'sqlite-thread'";
    command.ExecuteNonQuery();
    Assert(File.Exists(sqliteFallbackWal), "sqlite WAL is present after a journal-mode update");
    File.SetLastWriteTimeUtc(sqliteFallbackWal, DateTime.UtcNow.AddSeconds(1));
    sqliteFallbackUsage = sqliteFallbackService.Read(5.5m, sqliteFallbackRoot);
}
Assert(sqliteFallbackUsage.Models.Any(x => x.Model == "gpt-5.6-luna" && x.Tokens == 10) && sqliteFallbackUsage.Models.Any(x => x.Model == "gpt-5.6-terra" && x.Tokens == 15) && sqliteFallbackService.FilesRebuiltLastRead == 1, "changed sqlite fallback rebuilds only its affected rollout while later temporal settings remain authoritative");
var missingSqliteUsage = new LocalUsageAnalyticsService(() => analyticsNow, stateDatabasePath: Path.Combine(sqliteFallbackRoot, "missing.sqlite")).Read(5.5m, sqliteFallbackRoot);
Assert(missingSqliteUsage.MonthTokens == 32, "missing sqlite fallback leaves local analytics available");
var corruptSqlitePath = Path.Combine(sqliteFallbackRoot, "corrupt.sqlite");
File.WriteAllText(corruptSqlitePath, "not a sqlite database");
var corruptSqliteUsage = new LocalUsageAnalyticsService(() => analyticsNow, stateDatabasePath: corruptSqlitePath).Read(5.5m, sqliteFallbackRoot);
Assert(corruptSqliteUsage.MonthTokens == 32, "corrupt sqlite fallback leaves local analytics available");
using (var lockConnection = new SqliteConnection($"Data Source={sqliteFallbackDb};Pooling=False"))
{
    lockConnection.Open();
    using (var lockCommand = lockConnection.CreateCommand()) { lockCommand.CommandText = "BEGIN EXCLUSIVE"; lockCommand.ExecuteNonQuery(); }
    File.SetLastWriteTimeUtc(sqliteFallbackWal, DateTime.UtcNow.AddSeconds(2));
    var lockedSqliteUsage = sqliteFallbackService.Read(5.5m, sqliteFallbackRoot);
    Assert(lockedSqliteUsage.Models.Any(x => x.Model == "gpt-5.6-luna" && x.Tokens == 10) && sqliteFallbackService.FilesRebuiltLastRead == 0, "locked sqlite fallback preserves the last valid map without rebuilding rollouts as unknown");
    using var rollbackCommand = lockConnection.CreateCommand();
    rollbackCommand.CommandText = "ROLLBACK";
    rollbackCommand.ExecuteNonQuery();
}
Directory.Delete(sqliteFallbackRoot, true);
Directory.Delete(analyticsRoot, true);
var parallelAnalyticsRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-parallel-analytics-" + Guid.NewGuid());
Directory.CreateDirectory(parallelAnalyticsRoot);
for (var index = 0; index < 4; index++)
{
    var total = (index + 1) * 100;
    var contextLine = JsonSerializer.Serialize(new { timestamp = $"2026-08-12T1{index}:00:00Z", payload = new { type = "turn_context", model = $"gpt-5.6-model-{index}" } });
    var tokenLine = JsonSerializer.Serialize(new { timestamp = $"2026-08-12T1{index}:01:00Z", payload = new { type = "token_count", info = new { total_token_usage = new { input_tokens = total, cached_input_tokens = 0, output_tokens = 0, total_tokens = total } } } });
    File.WriteAllText(Path.Combine(parallelAnalyticsRoot, $"session-{index}.jsonl"), contextLine + "\n" + tokenLine + "\n");
}
var sequentialParallelFixtureService = new LocalUsageAnalyticsService(() => analyticsNow, maxParseParallelism: 1);
var boundedParallelFixtureService = new LocalUsageAnalyticsService(() => analyticsNow, maxParseParallelism: 2);
var sequentialParallelFixture = sequentialParallelFixtureService.Read(5.5m, parallelAnalyticsRoot);
var boundedParallelFixture = boundedParallelFixtureService.Read(5.5m, parallelAnalyticsRoot);
Assert(JsonSerializer.Serialize(boundedParallelFixture) == JsonSerializer.Serialize(sequentialParallelFixture) &&
       sequentialParallelFixtureService.FilesParsedLastRead == 4 && boundedParallelFixtureService.FilesParsedLastRead == 4 &&
       sequentialParallelFixtureService.FilesRebuiltLastRead == 4 && boundedParallelFixtureService.FilesRebuiltLastRead == 4 &&
       sequentialParallelFixtureService.BytesReadLastRead == boundedParallelFixtureService.BytesReadLastRead,
       "four independent cold files produce byte-for-byte identical analytics and counters with sequential and bounded-parallel parsing");
var analyticsServiceSource = File.ReadAllText(FindRepositoryFile("src", "CodexTracker.Core", "LocalUsageAnalyticsService.cs"));
Assert(analyticsServiceSource.Contains("ParseAggregate(plan.File.Path, offset, plan.Signature.Length", StringComparison.Ordinal) &&
       analyticsServiceSource.Contains("new BoundedReadStream(stream, Math.Max(0, snapshotLength - offset))", StringComparison.Ordinal) &&
       analyticsServiceSource.Contains("stream.Seek(length - 1, SeekOrigin.Begin);", StringComparison.Ordinal),
       "cold parsing is bounded to each planned file signature so concurrent writer growth is deferred instead of double-counted");
Directory.Delete(parallelAnalyticsRoot, true);
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
Assert(activeAgents.Single(x => x.ThreadId == "root-b").Model == "unknown" && activeAgents.Single(x => x.ThreadId == "root-b").Effort == "unknown", "missing turn context initially uses unknown metadata");
File.AppendAllText(rootBActivityPath, "\n{\"timestamp\":\"2026-08-14T13:29:57Z\",\"payload\":{\"type\":\"turn_context\",\"model\":\"gpt-5.6-sol\",\"effort\":\"high\"}}\n");
File.SetLastWriteTimeUtc(rootBActivityPath, activityNow.UtcDateTime.AddSeconds(1));
var appendedContextAgent = activityService.Read(null, activityRoot).Single(x => x.ThreadId == "root-b");
Assert(appendedContextAgent.Model == "gpt-5.6-sol" && appendedContextAgent.Effort == "high", "payload turn_context append replaces unknown metadata through the incremental cache");
var stagnantMtimeActivityPath = Path.Combine(activityRoot, "rollout-2026-08-14T13-29-00-stagnant.jsonl");
File.WriteAllText(stagnantMtimeActivityPath, """
{"timestamp":"2026-08-14T13:29:00Z","type":"session_meta","payload":{"session_id":"stagnant","id":"stagnant","thread_source":"user"}}
{"timestamp":"2026-08-14T13:29:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"stagnant-turn"}}
{"timestamp":"2026-08-14T13:29:02Z","payload":{"type":"turn_context","model":"gpt-5.6-terra","effort":"medium"}}
""");
File.SetLastWriteTimeUtc(stagnantMtimeActivityPath, activityNow.AddMinutes(-10).UtcDateTime);
var stagnantMtimeService = new AgentActivityService(() => activityNow);
var stagnantMtimeAgent = stagnantMtimeService.Read(null, activityRoot).Single(x => x.ThreadId == "stagnant");
Assert(stagnantMtimeAgent.Model == "gpt-5.6-terra" && stagnantMtimeAgent.Effort == "medium", "a current rollout with stale filesystem mtime is discovered from its session date and payload turn context");
File.AppendAllText(stagnantMtimeActivityPath, "\n{\"timestamp\":\"2026-08-14T13:29:59Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_reasoning\",\"text\":\"Evento incremental\"}}\n");
File.SetLastWriteTimeUtc(stagnantMtimeActivityPath, activityNow.AddMinutes(-10).UtcDateTime);
stagnantMtimeAgent = stagnantMtimeService.Read(null, activityRoot).Single(x => x.ThreadId == "stagnant");
Assert(stagnantMtimeAgent.Status == "Evento incremental" && stagnantMtimeAgent.Model == "gpt-5.6-terra" && stagnantMtimeAgent.Effort == "medium", "stale-mtime rollout observes appended bytes incrementally without losing metadata");
var localBoundaryRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-agent-activity-local-boundary-" + Guid.NewGuid());
Directory.CreateDirectory(localBoundaryRoot);
var localBoundaryWallClock = new DateTime(2026, 8, 14, 23, 58, 0, DateTimeKind.Unspecified);
var localBoundaryNow = new DateTimeOffset(localBoundaryWallClock, TimeZoneInfo.Local.GetUtcOffset(localBoundaryWallClock));
var localBoundaryPath = Path.Combine(localBoundaryRoot, $"rollout-{localBoundaryNow.LocalDateTime:yyyy-MM-dd}T23-58-00-local.jsonl");
File.WriteAllText(localBoundaryPath, $"{{\"timestamp\":\"{localBoundaryNow.UtcDateTime:O}\",\"type\":\"session_meta\",\"payload\":{{\"session_id\":\"local-boundary\",\"id\":\"local-boundary\"}}}}\n{{\"timestamp\":\"{localBoundaryNow.AddSeconds(1).UtcDateTime:O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"local-boundary-turn\"}}}}\n");
File.SetLastWriteTimeUtc(localBoundaryPath, localBoundaryNow.AddDays(-2).UtcDateTime);
Assert(new AgentActivityService(() => localBoundaryNow).Read(null, localBoundaryRoot).Single().ThreadId == "local-boundary", "rollout filename bootstrap uses the local calendar at the UTC date boundary");
Directory.Delete(localBoundaryRoot, true);
var evictionRoot = Path.Combine(Path.GetTempPath(), "codex-tracker-agent-activity-eviction-" + Guid.NewGuid());
Directory.CreateDirectory(evictionRoot);
var evictionPath = Path.Combine(evictionRoot, "old-rollout.jsonl");
File.WriteAllText(evictionPath, """
{"timestamp":"2026-08-14T13:29:00Z","type":"session_meta","payload":{"session_id":"evict","id":"evict","thread_source":"user"}}
{"timestamp":"2026-08-14T13:29:01Z","type":"event_msg","payload":{"type":"task_started","turn_id":"evict-turn"}}
""");
File.SetLastWriteTimeUtc(evictionPath, activityNow.UtcDateTime);
var evictionNow = activityNow;
var evictionService = new AgentActivityService(() => evictionNow);
Assert(evictionService.Read(null, evictionRoot).Single().ThreadId == "evict" && CachedRolloutCount(evictionService) == 1, "recent mtime rollout enters the activity cache");
evictionNow = activityNow.AddDays(2);
Assert(evictionService.Read(null, evictionRoot).Count == 0 && CachedRolloutCount(evictionService) == 0, "unchanged stale rollout is evicted instead of remaining cached indefinitely");
Directory.Delete(evictionRoot, true);
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
static int CachedRolloutCount(AgentActivityService service) =>
    ((System.Collections.IDictionary)(typeof(AgentActivityService).GetField("_cache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(service)
        ?? throw new MissingFieldException(typeof(AgentActivityService).FullName, "_cache"))).Count;
static bool NearlyEqual(double? actual, double expected) => actual is double value && Math.Abs(value - expected) < 0.000001;
static bool NearlyEqualInstant(DateTimeOffset? actual, DateTimeOffset expected) => actual is { } value && Math.Abs((value - expected).TotalMilliseconds) < 1;
static string FindRepositoryFile(params string[] segments)
{
    for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine([directory.FullName, .. segments]);
        if (File.Exists(candidate)) return candidate;
    }
    throw new FileNotFoundException("Repository source file not found.", Path.Combine(segments));
}

static void CopyFileSnapshot(string sourcePath, string destinationPath)
{
    using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    var remaining = source.Length;
    var buffer = new byte[128 * 1024];
    while (remaining > 0)
    {
        var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
        if (read == 0) break;
        destination.Write(buffer, 0, read);
        remaining -= read;
    }
}
