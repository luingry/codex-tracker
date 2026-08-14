using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using CodexTracker.Core;

namespace CodexTracker;

public sealed record RankingRow(string Model, string Tokens, double Fraction, bool Priced, string SecondaryText);
public sealed class AgentActivityRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string? _parentThreadId;
    private int _hierarchyDepth;
    private Thickness _indent;
    private string _sourceType = "", _sourceTitle = "", _sourceStatus = "";
    private string _type = "", _title = "", _status = "", _modelAndEffort = "", _elapsed = "";
    private bool _isWorkAnimationEnabled, _isNew;
    public AgentActivityRow(ActiveAgent agent, DateTimeOffset now, bool isNew)
    {
        ThreadId = agent.ThreadId;
        _isNew = isNew;
        Update(agent, now);
    }

    public string ThreadId { get; }
    public string? ParentThreadId => _parentThreadId;
    public int HierarchyDepth => _hierarchyDepth;
    public Thickness Indent => _indent;
    public string Type => _type;
    public string Title => _title;
    public string Status => _status;
    public string ModelAndEffort => _modelAndEffort;
    public string Elapsed => _elapsed;
    public bool IsWorkAnimationEnabled => _isWorkAnimationEnabled;
    public bool IsNew => _isNew;

    public void Update(ActiveAgent agent, DateTimeOffset now)
    {
        Set(ref _parentThreadId, agent.ParentThreadId, nameof(ParentThreadId));
        Set(ref _hierarchyDepth, agent.HierarchyDepth, nameof(HierarchyDepth));
        Set(ref _indent, new Thickness(Math.Min(48d, agent.HierarchyDepth * 12d), 0, 0, 0), nameof(Indent));
        _sourceType = agent.Type;
        _sourceTitle = agent.Title;
        _sourceStatus = agent.Status;
        RefreshLocalization();
        Set(ref _modelAndEffort, $"{agent.Model} · {agent.Effort}", nameof(ModelAndEffort));
        Set(ref _elapsed, FormatElapsed(now - agent.StartedAt), nameof(Elapsed));
        Set(ref _isWorkAnimationEnabled, SystemParameters.ClientAreaAnimation, nameof(IsWorkAnimationEnabled));
    }

    public void MarkStable() => Set(ref _isNew, false, nameof(IsNew));
    public void RefreshLocalization()
    {
        Set(ref _type, LocalizationManager.TranslateKnown(_sourceType), nameof(Type));
        Set(ref _title, LocalizationManager.TranslateKnown(_sourceTitle), nameof(Title));
        Set(ref _status, LocalizationManager.TranslateKnown(_sourceStatus), nameof(Status));
    }
    private void Set<T>(ref T field, T value, string name) { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new(name)); } }
    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        return elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes:00}m" : $"{elapsed.Minutes}m {elapsed.Seconds:00}s";
    }
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _weekly = "--", _weeklyTokens = "--", _weeklyCost = "--", _reset = LocalizationManager.Text("LoadingWeeklyQuota"), _today = "--", _month = "--", _cost = "--", _todayCost = "--", _monthCost = "--", _coverage = "", _forecast = LocalizationManager.Text("InsufficientData"), _status = LocalizationManager.Text("Loading"), _currencyCode = "BRL";
    private bool _expanded, _topmost, _isExhaustionRisk;
    private bool _hasActiveAgents, _isAgentListOpen;
    private int _activeAgentCount;
    private double _remainingPercent;
    private long? _weeklyTokenCount;
    private decimal _lastTodayUsd, _lastTodayBrl, _lastMonthUsd, _lastMonthBrl;
    private UsageWindowEstimate? _lastWeeklyEstimate;
    private UsageAnalytics? _lastAnalytics;
    private QuotaWindow? _lastWeekly;
    private WeeklyForecast? _lastForecast;
    private bool _hasQuotaSnapshot;
    private (DateTimeOffset Start, DateTimeOffset End)? _activeQuotaCycle;
    private RankingPeriod _rankingPeriod = RankingPeriod.Month;
    private bool _hasAnalytics;
    public ObservableCollection<RankingRow> Ranking { get; } = [];
    public ObservableCollection<AgentActivityRow> ActiveAgents { get; } = [];
    public string AppVersion { get; } = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0"}";
    public string Weekly { get => _weekly; set => Set(ref _weekly, value); }
    public string WeeklyTokens { get => _weeklyTokens; private set => Set(ref _weeklyTokens, value); }
    public long? WeeklyTokenCount { get => _weeklyTokenCount; private set => Set(ref _weeklyTokenCount, value); }
    public string WeeklyCost { get => _weeklyCost; private set => Set(ref _weeklyCost, value); }
    public IReadOnlyList<DailyTokenUsage> DailyTokenSeries { get; private set; } = [];
    public string Reset { get => _reset; set => Set(ref _reset, value); }
    public string Today { get => _today; set => Set(ref _today, value); }
    public string Month { get => _month; set => Set(ref _month, value); }
    public string Cost
    {
        get => _cost;
        set
        {
            if (!EqualityComparer<string>.Default.Equals(_cost, value))
            {
                _cost = value;
                PropertyChanged?.Invoke(this, new(nameof(Cost)));
                PropertyChanged?.Invoke(this, new(nameof(CostFormatted)));
            }
        }
    }
    public string CostFormatted => Cost;
    public string TodayCost { get => _todayCost; private set => Set(ref _todayCost, value); }
    public string MonthCost { get => _monthCost; private set => Set(ref _monthCost, value); }
    public string CurrencyCode { get => _currencyCode; private set => Set(ref _currencyCode, SettingsStore.NormalizeCurrency(value)); }
    public string Coverage { get => _coverage; set => Set(ref _coverage, value); }
    public string Forecast { get => _forecast; set => Set(ref _forecast, value); }
    public bool IsExhaustionRisk { get => _isExhaustionRisk; private set => Set(ref _isExhaustionRisk, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }
    public bool HasActiveAgents { get => _hasActiveAgents; private set => Set(ref _hasActiveAgents, value); }
    public bool IsAgentListOpen { get => _isAgentListOpen; set => Set(ref _isAgentListOpen, value); }
    public bool IsWorkAnimationEnabled => HasActiveAgents && SystemParameters.ClientAreaAnimation;
    public int ActiveAgentCount { get => _activeAgentCount; private set => Set(ref _activeAgentCount, value); }
    public double RemainingPercent { get => _remainingPercent; set => Set(ref _remainingPercent, value); }
    public bool IsRankingDay { get => _rankingPeriod == RankingPeriod.Day; set { if (value) SetRankingPeriod(RankingPeriod.Day); } }
    public bool IsRankingWeek { get => _rankingPeriod == RankingPeriod.Week; set { if (value) SetRankingPeriod(RankingPeriod.Week); } }
    public bool IsRankingMonth { get => _rankingPeriod == RankingPeriod.Month; set { if (value) SetRankingPeriod(RankingPeriod.Month); } }

    public void ApplyAgents(IReadOnlyList<ActiveAgent> agents, DateTimeOffset now, bool animateNewRows, bool? animationsEnabled = null)
    {
        var hasActiveAgents = agents.Count > 0;
        var existing = ActiveAgents.ToDictionary(row => row.ThreadId, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<AgentActivityRow>(agents.Count);
        foreach (var agent in agents)
        {
            if (existing.TryGetValue(agent.ThreadId, out var row)) row.Update(agent, now);
            else row = new AgentActivityRow(agent, now, animateNewRows && (animationsEnabled ?? SystemParameters.ClientAreaAnimation));
            ordered.Add(row);
        }
        for (var index = 0; index < ordered.Count; index++)
        {
            if (index < ActiveAgents.Count && ReferenceEquals(ActiveAgents[index], ordered[index])) continue;
            var currentIndex = ActiveAgents.IndexOf(ordered[index]);
            if (currentIndex >= 0) ActiveAgents.Move(currentIndex, index);
            else ActiveAgents.Insert(index, ordered[index]);
        }
        while (ActiveAgents.Count > ordered.Count) ActiveAgents.RemoveAt(ActiveAgents.Count - 1);
        ActiveAgentCount = agents.Count;
        HasActiveAgents = hasActiveAgents;
        PropertyChanged?.Invoke(this, new(nameof(IsWorkAnimationEnabled)));
    }

    public void MarkNewAgentRowsStable()
    {
        foreach (var row in ActiveAgents.Where(row => row.IsNew)) row.MarkStable();
    }

    public void ApplyQuota(RateLimitSnapshot snapshot)
    {
        var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
        _lastWeekly = weekly;
        _hasQuotaSnapshot = true;
        UpdateActiveQuotaCycle(weekly);
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? LocalizationManager.Text("QuotaNotReported") : ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now, LocalizationManager.CurrentLanguageCode);
        ApplyForecast(WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt));
        Status = weekly is null ? LocalizationManager.Text("NoLiveData") : LocalizationManager.Text("LiveData");
    }

    public void SetCurrency(string? currencyCode)
    {
        CurrencyCode = CurrencyPresentation.Normalize(currencyCode);
        RefreshFormattedCosts();
        RefreshRanking();
    }

    public void Apply(RateLimitSnapshot snapshot, UsageAnalytics analytics, string? currencyCode = null)
    {
        var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
        _lastWeekly = weekly;
        _hasQuotaSnapshot = true;
        UpdateActiveQuotaCycle(weekly);
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? LocalizationManager.Text("QuotaNotReported") : ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now, LocalizationManager.CurrentLanguageCode);
        Today = TokenPresentation.Format(analytics.TodayTokens, LocalizationManager.CurrentLanguageCode); Month = TokenPresentation.Format(analytics.MonthTokens, LocalizationManager.CurrentLanguageCode);
        DailyTokenSeries = analytics.DailySeries ?? [];
        PropertyChanged?.Invoke(this, new(nameof(DailyTokenSeries)));
        _lastWeeklyEstimate = analytics.EstimateInWeeklyWindow(weekly);
        _lastAnalytics = analytics;
        WeeklyTokenCount = _lastWeeklyEstimate?.Tokens;
        WeeklyTokens = WeeklyTokenCount is { } weeklyTokens ? TokenPresentation.Format(weeklyTokens, LocalizationManager.CurrentLanguageCode) : "--";
        _lastTodayUsd = analytics.TodayUsd; _lastTodayBrl = analytics.TodayBrl;
        _lastMonthUsd = analytics.MonthUsd; _lastMonthBrl = analytics.MonthBrl;
        _hasAnalytics = true;
        if (currencyCode is not null) CurrencyCode = CurrencyPresentation.Normalize(currencyCode);
        RefreshFormattedCosts();
        ApplyForecast(WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt));
        RefreshRanking();
        Status = weekly is null ? LocalizationManager.Text("NoLiveData") : LocalizationManager.Text("LiveData");
    }

    private void SetRankingPeriod(RankingPeriod period)
    {
        if (_rankingPeriod == period) return;
        _rankingPeriod = period;
        PropertyChanged?.Invoke(this, new(nameof(IsRankingDay)));
        PropertyChanged?.Invoke(this, new(nameof(IsRankingWeek)));
        PropertyChanged?.Invoke(this, new(nameof(IsRankingMonth)));
        RefreshRanking();
    }

    private void RefreshRanking()
    {
        if (_lastAnalytics is null) return;
        var now = DateTimeOffset.Now;
        IReadOnlyList<ModelUsage> models = _rankingPeriod switch
        {
            RankingPeriod.Day => _lastAnalytics.ModelsInWindow(new DateTimeOffset(now.LocalDateTime.Date, now.Offset), now),
            RankingPeriod.Week when _activeQuotaCycle is { } cycle => _lastAnalytics.ModelsInWindow(cycle.Start, cycle.End),
            RankingPeriod.Week => [],
            _ => _lastAnalytics.Models
        };
        Ranking.Clear();
        var max = Math.Max(1, models.Take(5).Select(x => x.Tokens).DefaultIfEmpty().Max());
        foreach (var item in models.Take(5))
        {
            var secondaryText = item.Priced
                ? CurrencyPresentation.FormatCost(item.CostUsd, item.CostUsd * _lastAnalytics.UsdBrl, CurrencyCode, LocalizationManager.CurrentLanguageCode)
                : LocalizationManager.Text("NoTariff");
            Ranking.Add(new(item.Model, TokenPresentation.Format(item.Tokens, LocalizationManager.CurrentLanguageCode), item.Tokens / (double)max, item.Priced, secondaryText));
        }
    }
    private void ApplyForecast(WeeklyForecast forecast)
    {
        _lastForecast = forecast;
        IsExhaustionRisk = forecast.Status == "Risco de esgotar antes do reset";
        var status = LocalizationManager.TranslateKnown(forecast.Status);
        Forecast = forecast.ProjectedPercent is double projected
            ? LocalizationManager.Format("ProjectedForecast", status, WeeklyForecastCalculator.FormatProjectedPercent(projected, LocalizationManager.CurrentLanguageCode))
            : status;
    }
    private void UpdateActiveQuotaCycle(QuotaWindow? weekly)
    {
        _activeQuotaCycle = weekly?.ResetsAt is { } end && weekly.WindowDurationMins is > 0
            ? (end.AddMinutes(-weekly.WindowDurationMins.Value), end)
            : null;
        RefreshRanking();
    }
    private void RefreshFormattedCosts()
    {
        if (!_hasAnalytics) return;
        WeeklyCost = _lastWeeklyEstimate is { } estimate ? CurrencyPresentation.FormatCost(estimate.CostUsd, estimate.CostBrl, CurrencyCode, LocalizationManager.CurrentLanguageCode) : "--";
        TodayCost = CurrencyPresentation.FormatCost(_lastTodayUsd, _lastTodayBrl, CurrencyCode, LocalizationManager.CurrentLanguageCode);
        MonthCost = CurrencyPresentation.FormatCost(_lastMonthUsd, _lastMonthBrl, CurrencyCode, LocalizationManager.CurrentLanguageCode);
        Cost = MonthCost;
    }
    public void RefreshLocalization()
    {
        if (_hasQuotaSnapshot)
            Reset = _lastWeekly is null ? LocalizationManager.Text("QuotaNotReported") : ResetCountdown.Format(_lastWeekly.ResetsAt, DateTimeOffset.Now, LocalizationManager.CurrentLanguageCode);
        else
            Reset = LocalizationManager.Text("LoadingWeeklyQuota");
        if (_lastAnalytics is { } analytics)
        {
            Today = TokenPresentation.Format(analytics.TodayTokens, LocalizationManager.CurrentLanguageCode);
            Month = TokenPresentation.Format(analytics.MonthTokens, LocalizationManager.CurrentLanguageCode);
            WeeklyTokens = WeeklyTokenCount is { } weeklyTokens ? TokenPresentation.Format(weeklyTokens, LocalizationManager.CurrentLanguageCode) : "--";
        }
        if (_lastForecast is { } forecast) ApplyForecast(forecast);
        else Forecast = LocalizationManager.Text("InsufficientData");
        Status = LocalizationManager.TranslateKnown(Status);
        foreach (var row in ActiveAgents) row.RefreshLocalization();
        RefreshRanking();
        RefreshFormattedCosts();
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "") { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new(name)); } }

    private enum RankingPeriod { Day, Week, Month }
}
