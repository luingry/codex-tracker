using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using CodexTracker.Core;

namespace CodexTracker;

public sealed record RankingRow(string Model, string Tokens, double Fraction, bool Priced, string TariffNote);

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _weekly = "--", _weeklyTokens = "--", _weeklyCost = "--", _reset = "Carregando quota semanal", _today = "--", _month = "--", _cost = "--", _todayCost = "--", _monthCost = "--", _coverage = "", _forecast = "Dados insuficientes", _status = "Carregando", _currencyCode = "BRL";
    private bool _expanded, _topmost, _isExhaustionRisk;
    private double _remainingPercent;
    private long? _weeklyTokenCount;
    private decimal _lastTodayUsd, _lastTodayBrl, _lastMonthUsd, _lastMonthBrl;
    private UsageWindowEstimate? _lastWeeklyEstimate;
    private UsageAnalytics? _lastAnalytics;
    private (DateTimeOffset Start, DateTimeOffset End)? _activeQuotaCycle;
    private RankingPeriod _rankingPeriod = RankingPeriod.Month;
    private bool _hasAnalytics;
    public ObservableCollection<RankingRow> Ranking { get; } = [];
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
    public double RemainingPercent { get => _remainingPercent; set => Set(ref _remainingPercent, value); }
    public bool IsRankingDay { get => _rankingPeriod == RankingPeriod.Day; set { if (value) SetRankingPeriod(RankingPeriod.Day); } }
    public bool IsRankingWeek { get => _rankingPeriod == RankingPeriod.Week; set { if (value) SetRankingPeriod(RankingPeriod.Week); } }
    public bool IsRankingMonth { get => _rankingPeriod == RankingPeriod.Month; set { if (value) SetRankingPeriod(RankingPeriod.Month); } }

    public void ApplyQuota(RateLimitSnapshot snapshot)
    {
        var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
        UpdateActiveQuotaCycle(weekly);
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? "Quota semanal não reportada" : ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now);
        ApplyForecast(WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt));
        Status = weekly is null ? "Sem dados ao vivo" : "Ao vivo via Codex";
    }

    public void SetCurrency(string? currencyCode)
    {
        CurrencyCode = CurrencyPresentation.Normalize(currencyCode);
        RefreshFormattedCosts();
    }

    public void Apply(RateLimitSnapshot snapshot, UsageAnalytics analytics, string? currencyCode = null)
    {
        var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
        UpdateActiveQuotaCycle(weekly);
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? "Quota semanal não reportada" : ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now);
        Today = TokenPresentation.Format(analytics.TodayTokens); Month = TokenPresentation.Format(analytics.MonthTokens);
        DailyTokenSeries = analytics.DailySeries ?? [];
        PropertyChanged?.Invoke(this, new(nameof(DailyTokenSeries)));
        _lastWeeklyEstimate = analytics.EstimateInWeeklyWindow(weekly);
        _lastAnalytics = analytics;
        WeeklyTokenCount = _lastWeeklyEstimate?.Tokens;
        WeeklyTokens = WeeklyTokenCount is { } weeklyTokens ? TokenPresentation.Format(weeklyTokens) : "--";
        _lastTodayUsd = analytics.TodayUsd; _lastTodayBrl = analytics.TodayBrl;
        _lastMonthUsd = analytics.MonthUsd; _lastMonthBrl = analytics.MonthBrl;
        _hasAnalytics = true;
        if (currencyCode is not null) CurrencyCode = CurrencyPresentation.Normalize(currencyCode);
        RefreshFormattedCosts();
        ApplyForecast(WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt));
        RefreshRanking();
        Status = weekly is null ? "Sem dados ao vivo" : "Ao vivo via Codex";
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
        foreach (var item in models.Take(5)) Ranking.Add(new(item.Model, TokenPresentation.Format(item.Tokens), item.Tokens / (double)max, item.Priced, item.Priced ? "" : "sem tarifa"));
    }
    private void ApplyForecast(WeeklyForecast forecast)
    {
        IsExhaustionRisk = forecast.Status == "Risco de esgotar antes do reset";
        Forecast = forecast.ProjectedPercent is double projected
            ? $"{forecast.Status} · {WeeklyForecastCalculator.FormatProjectedPercent(projected)} projetado"
            : forecast.Status;
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
        WeeklyCost = _lastWeeklyEstimate is { } estimate ? CurrencyPresentation.FormatCost(estimate.CostUsd, estimate.CostBrl, CurrencyCode) : "--";
        TodayCost = CurrencyPresentation.FormatCost(_lastTodayUsd, _lastTodayBrl, CurrencyCode);
        MonthCost = CurrencyPresentation.FormatCost(_lastMonthUsd, _lastMonthBrl, CurrencyCode);
        Cost = MonthCost;
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string name = "") { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new(name)); } }

    private enum RankingPeriod { Day, Week, Month }
}
