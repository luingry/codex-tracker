using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexTracker.Core;

namespace CodexTracker;

public sealed record RankingRow(string Model, string Tokens, double Fraction, bool Priced, string TariffNote);

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _weekly = "--", _weeklyTokens = "--", _weeklyCost = "--", _reset = "Carregando quota semanal", _today = "--", _month = "--", _cost = "--", _todayCost = "--", _monthCost = "--", _coverage = "", _forecast = "Dados insuficientes", _status = "Carregando", _currencyCode = "BRL";
    private bool _expanded, _topmost;
    private double _remainingPercent;
    private long? _weeklyTokenCount;
    private decimal _lastTodayUsd, _lastTodayBrl, _lastMonthUsd, _lastMonthBrl;
    private UsageWindowEstimate? _lastWeeklyEstimate;
    private bool _hasAnalytics;
    public ObservableCollection<RankingRow> Ranking { get; } = [];
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
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool Expanded { get => _expanded; set => Set(ref _expanded, value); }
    public bool Topmost { get => _topmost; set => Set(ref _topmost, value); }
    public double RemainingPercent { get => _remainingPercent; set => Set(ref _remainingPercent, value); }

    public void ApplyQuota(RateLimitSnapshot snapshot)
    {
        var weekly = snapshot.Windows.FirstOrDefault(x => x.Id == "codex:primary" && x.WindowDurationMins >= 10000);
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? "Quota semanal não reportada" : $"restante esta semana · {ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now)}";
        var forecast = WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt);
        Forecast = forecast.ProjectedPercent is double projected ? $"{forecast.Status} · {WeeklyForecastCalculator.FormatProjectedPercent(projected)} projetado" : forecast.Status;
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
        Weekly = QuotaPresentation.FormatWeeklyRemaining(weekly);
        RemainingPercent = weekly?.RemainingPercent ?? 0;
        Reset = weekly is null ? "Quota semanal não reportada" : $"restante esta semana · {ResetCountdown.Format(weekly.ResetsAt, DateTimeOffset.Now)}";
        Today = TokenPresentation.Format(analytics.TodayTokens); Month = TokenPresentation.Format(analytics.MonthTokens);
        DailyTokenSeries = analytics.DailySeries ?? [];
        PropertyChanged?.Invoke(this, new(nameof(DailyTokenSeries)));
        _lastWeeklyEstimate = analytics.EstimateInWeeklyWindow(weekly);
        WeeklyTokenCount = _lastWeeklyEstimate?.Tokens;
        WeeklyTokens = WeeklyTokenCount is { } weeklyTokens ? TokenPresentation.Format(weeklyTokens) : "--";
        _lastTodayUsd = analytics.TodayUsd; _lastTodayBrl = analytics.TodayBrl;
        _lastMonthUsd = analytics.MonthUsd; _lastMonthBrl = analytics.MonthBrl;
        _hasAnalytics = true;
        if (currencyCode is not null) CurrencyCode = CurrencyPresentation.Normalize(currencyCode);
        RefreshFormattedCosts();
        var forecast = WeeklyForecastCalculator.Calculate(weekly, snapshot.ReceivedAt);
        Forecast = forecast.ProjectedPercent is double projected ? $"{forecast.Status} · {WeeklyForecastCalculator.FormatProjectedPercent(projected)} projetado" : forecast.Status;
        Ranking.Clear(); var max = Math.Max(1, analytics.Models.Take(5).Select(x => x.Tokens).DefaultIfEmpty().Max());
        foreach (var item in analytics.Models.Take(5)) Ranking.Add(new(item.Model, TokenPresentation.Format(item.Tokens), item.Tokens / (double)max, item.Priced, item.Priced ? "" : "sem tarifa"));
        Status = weekly is null ? "Sem dados ao vivo" : "Ao vivo via Codex";
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
}
