using System.IO;
using System.Text.Json;
using CodexTracker.Core;
using CodexTracker;

var compactSize = WidgetSizePolicy.Normalize(WidgetVisualMode.Compact, new WidgetSize(999, 1));
Assert(compactSize.Width == 320 && Math.Abs(compactSize.Height * 62 - compactSize.Width * 52) < 0.001, "compact size clamps width and preserves the 62:52 ratio");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(1, 999)) == new WidgetSize(300, 720), "detailed size keeps fixed width and clamps visible height");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 300)) == new WidgetSize(300, 300) && WidgetSizePolicy.Normalize(WidgetVisualMode.Detailed, new WidgetSize(300, 1)) == new WidgetSize(300, 260), "detailed preserves resized heights down to 260 and clamps below it");
Assert(WidgetSizePolicy.Normalize(WidgetVisualMode.Settings, new WidgetSize(double.NaN, 0)) == WidgetSizePolicy.Default(WidgetVisualMode.Settings), "invalid settings size falls back to its safe default");
var independentSlots = WidgetSizePolicy.NormalizeSlots(new WidgetModeSizes(new(124, 10), new(300, 480), new(300, 600)), false, new(62, 52));
Assert(WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Compact) == new WidgetSize(124, 124 / (62d / 52d)) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(independentSlots, WidgetVisualMode.Settings) == new WidgetSize(300, 600), "compact, detailed, and settings slots remain independent");
var conceptualTransition = WidgetSizePolicy.With(independentSlots, WidgetVisualMode.Compact, new(200, 1));
Assert(WidgetSizePolicy.Get(conceptualTransition, WidgetVisualMode.Compact) == new WidgetSize(200, 200 / (62d / 52d)) && WidgetSizePolicy.Get(conceptualTransition, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(conceptualTransition, WidgetVisualMode.Settings) == new WidgetSize(300, 600), "mode transition saves only its origin slot and restores other slots");
var compactDetailedCompact = new WidgetModeSizes(new(124, 1), new(300, 533), new(300, 600));
var restoredCompact = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Compact);
var restoredDetailed = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Detailed);
var restoredCompactAgain = WidgetSizePolicy.SelectModeSize(compactDetailedCompact, WidgetVisualMode.Compact);
Assert(restoredCompact == new WidgetSize(124, 124 / (62d / 52d)) && restoredDetailed == new WidgetSize(300, 533) && restoredCompactAgain == restoredCompact, "compact 124 survives repeated compact-detailed-compact selection without transient detailed dimensions");
var legacyCompact = SettingsStore.Normalize(new AppSettings(Width: 124, Height: 99, IsExpanded: false));
Assert(WidgetSizePolicy.Get(legacyCompact.ModeSizes!, WidgetVisualMode.Compact) == new WidgetSize(124, 124 / (62d / 52d)) && WidgetSizePolicy.Get(legacyCompact.ModeSizes!, WidgetVisualMode.Detailed) == WidgetSizePolicy.Default(WidgetVisualMode.Detailed), "legacy compact JSON migrates its dimensions only into compact");
var legacyDetailed = SettingsStore.Normalize(new AppSettings(Width: 222, Height: 480, IsExpanded: true));
Assert(WidgetSizePolicy.Get(legacyDetailed.ModeSizes!, WidgetVisualMode.Detailed) == new WidgetSize(300, 480) && WidgetSizePolicy.Get(legacyDetailed.ModeSizes!, WidgetVisualMode.Compact) == WidgetSizePolicy.Default(WidgetVisualMode.Compact), "legacy detailed JSON migrates its dimensions only into detailed");
var missingSizes = SettingsStore.Normalize(new AppSettings(ModeSizes: null));
Assert(missingSizes.ModeSizes is not null && WidgetSizePolicy.Get(missingSizes.ModeSizes, WidgetVisualMode.Settings) == WidgetSizePolicy.Default(WidgetVisualMode.Settings), "absent mode slots receive safe defaults");
var serializedSettings = legacyDetailed with { ModeSizes = new WidgetModeSizes(new(100, 1), new(300, 500), new(300, 650)) };
var roundTrippedSettings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(SettingsStore.Normalize(serializedSettings)))!;
Assert(WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Compact) == new WidgetSize(100, 100 / (62d / 52d)) && WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Detailed) == new WidgetSize(300, 500) && WidgetSizePolicy.Get(roundTrippedSettings.ModeSizes!, WidgetVisualMode.Settings) == new WidgetSize(300, 650), "new JSON round trip retains all three size slots");

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
var currencyAnalytics = new UsageAnalytics(10, 20, 2m, 10m, 100, [], .5m, 2.5m, [new(DateOnly.FromDateTime(DateTime.Today), 10, .5m, 2.5m)], [new(currencyTimelineAt, 10, 1m)], 5m);
currencyViewModel.Apply(new RateLimitSnapshot([new("codex:primary", "Weekly", 10, currencyReset, 10080)], null, null, null, DateTimeOffset.UtcNow), currencyAnalytics, "BRL");
Assert(currencyViewModel.WeeklyCost == "R$ 5,00" && currencyViewModel.TodayCost == "R$ 2,50" && currencyViewModel.MonthCost == "R$ 10,00", "view model initially formats all retained costs in BRL");
currencyViewModel.SetCurrency("USD");
Assert(currencyViewModel.CurrencyCode == "USD" && currencyViewModel.WeeklyCost == "US$ 1,00" && currencyViewModel.TodayCost == "US$ 0,50" && currencyViewModel.MonthCost == "US$ 2,00", "currency change immediately reformats retained weekly, daily and monthly costs without analytics refresh");
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
var analytics = new LocalUsageAnalyticsService().Read(5.5m, analyticsRoot);
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
Console.WriteLine("All CodexTracker core tests passed.");

static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("Test failed: " + message); }
static bool NearlyEqual(double? actual, double expected) => actual is double value && Math.Abs(value - expected) < 0.000001;
static bool NearlyEqualInstant(DateTimeOffset? actual, DateTimeOffset expected) => actual is { } value && Math.Abs((value - expected).TotalMilliseconds) < 1;
