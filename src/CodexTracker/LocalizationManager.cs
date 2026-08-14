using System.Globalization;
using System.Windows;

namespace CodexTracker;

/// <summary>Provides the application strings supported by the tracker UI.</summary>
public static class LocalizationManager
{
    public const string DefaultLanguageCode = "pt-BR";
    private const string EnglishLanguageCode = "en-US";
    private const string ResourcePrefix = "Loc.";

    private static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AppTitle"] = "Codex Tracker",
        ["Settings"] = "Configurações",
        ["CloseSettings"] = "Fechar configurações",
        ["DetailedMode"] = "Modo detalhado",
        ["ToggleDetailedMode"] = "Alternar modo compacto/detalhado",
        ["AlwaysOnTop"] = "Sempre no topo",
        ["Hide"] = "Ocultar",
        ["Show"] = "Mostrar",
        ["Refresh"] = "Atualizar",
        ["Exit"] = "Sair",
        ["DarkTheme"] = "Tema escuro",
        ["ToggleTheme"] = "Alternar tema claro ou escuro",
        ["AccentColor"] = "Cor de destaque",
        ["ChooseAccentColor"] = "Escolher cor de destaque",
        ["ChooseAccentColorTooltip"] = "Escolher a cor usada nos destaques",
        ["Language"] = "Idioma",
        ["Currency"] = "Moeda",
        ["UsdBrlRate"] = "Taxa USD para BRL (manual)",
        ["CodexPath"] = "Caminho do Codex",
        ["Browse"] = "Procurar",
        ["AutoDetect"] = "Auto",
        ["Test"] = "Testar",
        ["OpenLog"] = "Abrir log",
        ["Cancel"] = "Cancelar",
        ["Apply"] = "Aplicar",
        ["Agent"] = "Agent",
        ["Subagent"] = "Subagent",
        ["AgentsWorking"] = "{0} agentes trabalhando",
        ["AgentsWorkingTooltip"] = "Agentes trabalhando",
        ["ShowWorkingAgents"] = "Exibir agentes trabalhando",
        ["OpenAgentChat"] = "Abrir chat do agente",
        ["WeeklyQuotaIndicator"] = "Indicador de quota semanal restante",
        ["Working"] = "Em andamento",
        ["Waiting"] = "Aguardando",
        ["Completed"] = "Concluído",
        ["Failed"] = "Falhou",
        ["Cancelled"] = "Cancelado",
        ["Unknown"] = "Desconhecido",
        ["UnregisteredModel"] = "Modelo não registrado",
        ["Model"] = "Modelo",
        ["Effort"] = "Esforço",
        ["RunningFor"] = "Em execução há {0}",
        ["UsageRanking"] = "Ranking de uso",
        ["Day"] = "dia",
        ["Week"] = "semana",
        ["Month"] = "mês",
        ["Weekly"] = "SEMANAL",
        ["TodayUpper"] = "HOJE",
        ["WeekUpper"] = "SEMANA",
        ["MonthUpper"] = "MÊS",
        ["RankingPeriodDay"] = "Período do ranking: dia",
        ["RankingPeriodWeek"] = "Período do ranking: semana",
        ["RankingPeriodMonth"] = "Período do ranking: mês",
        ["DailyUsageCurrentMonth"] = "USO DIÁRIO · MÊS ATUAL",
        ["Quota"] = "Quota",
        ["WeeklyQuota"] = "Quota semanal",
        ["QuotaNotReported"] = "Quota semanal não reportada",
        ["LoadingWeeklyQuota"] = "Carregando quota semanal",
        ["Loading"] = "Carregando",
        ["InsufficientData"] = "Dados insuficientes para previsão",
        ["ShouldLastUntilReset"] = "Deve durar até o reset",
        ["LimitExhausted"] = "Limite esgotado",
        ["ExhaustionRisk"] = "Risco de esgotar antes do reset",
        ["ProjectedForecast"] = "{0} · {1} projetado",
        ["Reset"] = "Redefinição",
        ["LiveData"] = "Dados ao vivo via Codex",
        ["NoLiveData"] = "Sem dados ao vivo",
        ["Costs"] = "Custos",
        ["NoTariff"] = "sem tarifa",
        ["Connected"] = "Conectado",
        ["ConnectionError"] = "Erro de conexão",
        ["ConnectingToCodex"] = "Conectando ao Codex",
        ["StaleReconnecting"] = "Desatualizado: reconectando",
        ["StaleRetrying"] = "Desatualizado: tentando reconectar",
        ["TestFailed"] = "Teste falhou",
        ["CodexNotFound"] = "Erro: Codex CLI não encontrado",
        ["ConnectedWeeklyUnavailable"] = "Conectado - quota semanal indisponível",
        ["DayNumber"] = "Dia {0}",
        ["CodexConversation"] = "Conversa Codex",
        ["ConnectedWeeklyRemaining"] = "Conectado - quota semanal restante {0}"
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AppTitle"] = "Codex Tracker",
        ["Settings"] = "Settings",
        ["CloseSettings"] = "Close settings",
        ["DetailedMode"] = "Detailed mode",
        ["ToggleDetailedMode"] = "Toggle compact/detailed mode",
        ["AlwaysOnTop"] = "Always on top",
        ["Hide"] = "Hide",
        ["Show"] = "Show",
        ["Refresh"] = "Refresh",
        ["Exit"] = "Exit",
        ["DarkTheme"] = "Dark theme",
        ["ToggleTheme"] = "Toggle light or dark theme",
        ["AccentColor"] = "Accent color",
        ["ChooseAccentColor"] = "Choose accent color",
        ["ChooseAccentColorTooltip"] = "Choose the color used for highlights",
        ["Language"] = "Language",
        ["Currency"] = "Currency",
        ["UsdBrlRate"] = "USD to BRL rate (manual)",
        ["CodexPath"] = "Codex path",
        ["Browse"] = "Browse",
        ["AutoDetect"] = "Auto",
        ["Test"] = "Test",
        ["OpenLog"] = "Open log",
        ["Cancel"] = "Cancel",
        ["Apply"] = "Apply",
        ["Agent"] = "Agent",
        ["Subagent"] = "Subagent",
        ["AgentsWorking"] = "{0} agents working",
        ["AgentsWorkingTooltip"] = "Agents working",
        ["ShowWorkingAgents"] = "Show working agents",
        ["OpenAgentChat"] = "Open agent chat",
        ["WeeklyQuotaIndicator"] = "Weekly quota remaining indicator",
        ["Working"] = "Working",
        ["Waiting"] = "Waiting",
        ["Completed"] = "Completed",
        ["Failed"] = "Failed",
        ["Cancelled"] = "Cancelled",
        ["Unknown"] = "Unknown",
        ["UnregisteredModel"] = "Model not recorded",
        ["Model"] = "Model",
        ["Effort"] = "Effort",
        ["RunningFor"] = "Running for {0}",
        ["UsageRanking"] = "Usage ranking",
        ["Day"] = "day",
        ["Week"] = "week",
        ["Month"] = "month",
        ["Weekly"] = "WEEKLY",
        ["TodayUpper"] = "TODAY",
        ["WeekUpper"] = "WEEK",
        ["MonthUpper"] = "MONTH",
        ["RankingPeriodDay"] = "Ranking period: day",
        ["RankingPeriodWeek"] = "Ranking period: week",
        ["RankingPeriodMonth"] = "Ranking period: month",
        ["DailyUsageCurrentMonth"] = "DAILY USAGE · CURRENT MONTH",
        ["Quota"] = "Quota",
        ["WeeklyQuota"] = "Weekly quota",
        ["QuotaNotReported"] = "Weekly quota not reported",
        ["LoadingWeeklyQuota"] = "Loading weekly quota",
        ["Loading"] = "Loading",
        ["InsufficientData"] = "Insufficient data for forecast",
        ["ShouldLastUntilReset"] = "Should last until reset",
        ["LimitExhausted"] = "Limit exhausted",
        ["ExhaustionRisk"] = "Risk of exhaustion before reset",
        ["ProjectedForecast"] = "{0} · {1} projected",
        ["Reset"] = "Reset",
        ["LiveData"] = "Live data via Codex",
        ["NoLiveData"] = "No live data",
        ["Costs"] = "Costs",
        ["NoTariff"] = "no tariff",
        ["Connected"] = "Connected",
        ["ConnectionError"] = "Connection error",
        ["ConnectingToCodex"] = "Connecting to Codex",
        ["StaleReconnecting"] = "Stale: reconnecting",
        ["StaleRetrying"] = "Stale: retrying connection",
        ["TestFailed"] = "Test failed",
        ["CodexNotFound"] = "Error: Codex CLI not found",
        ["ConnectedWeeklyUnavailable"] = "Connected - weekly quota unavailable",
        ["DayNumber"] = "Day {0}",
        ["CodexConversation"] = "Codex conversation",
        ["ConnectedWeeklyRemaining"] = "Connected - weekly quota remaining {0}"
    };

    private static readonly IReadOnlyDictionary<string, string> KnownTextKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Agent"] = "Agent",
        ["Agente"] = "Agent",
        ["Subagent"] = "Subagent",
        ["Subagente"] = "Subagent",
        ["Working"] = "Working",
        ["Em andamento"] = "Working",
        ["Trabalhando"] = "Working",
        ["Waiting"] = "Waiting",
        ["Aguardando"] = "Waiting",
        ["Completed"] = "Completed",
        ["Concluído"] = "Completed",
        ["Failed"] = "Failed",
        ["Falhou"] = "Failed",
        ["Cancelled"] = "Cancelled",
        ["Cancelado"] = "Cancelled",
        ["Unknown"] = "Unknown",
        ["Desconhecido"] = "Unknown",
        ["Risco de esgotar antes do reset"] = "ExhaustionRisk",
        ["Risk of exhaustion before reset"] = "ExhaustionRisk",
        ["Dados insuficientes"] = "InsufficientData",
        ["Insufficient data"] = "InsufficientData",
        ["Deve durar até o reset"] = "ShouldLastUntilReset",
        ["Should last until reset"] = "ShouldLastUntilReset",
        ["Limite esgotado"] = "LimitExhausted",
        ["Limit exhausted"] = "LimitExhausted",
        ["Conversa Codex"] = "CodexConversation",
        ["Codex conversation"] = "CodexConversation",
        ["Sem dados ao vivo"] = "NoLiveData",
        ["No live data"] = "NoLiveData",
        ["Ao vivo via Codex"] = "LiveData",
        ["Dados ao vivo via Codex"] = "LiveData",
        ["Live via Codex"] = "LiveData",
        ["Erro de conexão"] = "ConnectionError",
        ["Connection error"] = "ConnectionError",
        ["Carregando"] = "Loading",
        ["Loading"] = "Loading",
        ["Conectando ao Codex"] = "ConnectingToCodex",
        ["Connecting to Codex"] = "ConnectingToCodex",
        ["Desatualizado: reconectando"] = "StaleReconnecting",
        ["Stale: reconnecting"] = "StaleReconnecting",
        ["Desatualizado: tentando reconectar"] = "StaleRetrying",
        ["Stale: retrying connection"] = "StaleRetrying",
        ["Teste falhou"] = "TestFailed",
        ["Test failed"] = "TestFailed",
        ["Erro: Codex CLI não encontrado"] = "CodexNotFound",
        ["Error: Codex CLI not found"] = "CodexNotFound",
        ["Conectado - quota semanal indisponível"] = "ConnectedWeeklyUnavailable",
        ["Connected - weekly quota unavailable"] = "ConnectedWeeklyUnavailable"
    };

    private static string _languageCode = DefaultLanguageCode;

    public static string CurrentLanguageCode => _languageCode;

    public static string NormalizeLanguage(string? languageCode) =>
        string.Equals(languageCode?.Trim(), EnglishLanguageCode, StringComparison.OrdinalIgnoreCase)
            ? EnglishLanguageCode
            : DefaultLanguageCode;

    public static void Apply(string? languageCode)
    {
        _languageCode = NormalizeLanguage(languageCode);
        var strings = CurrentStrings;
        if (System.Windows.Application.Current is not { } application) return;

        foreach (var item in Portuguese)
            application.Resources[ResourcePrefix + item.Key] = strings.TryGetValue(item.Key, out var text) ? text : item.Value;
    }

    public static string Text(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        return CurrentStrings.TryGetValue(key, out var text)
            ? text
            : Portuguese.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static bool HasTextKey(string key) => !string.IsNullOrWhiteSpace(key) && Portuguese.ContainsKey(key) && English.ContainsKey(key);

    public static string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.GetCultureInfo(_languageCode), Text(key), arguments ?? Array.Empty<object>());

    public static string TranslateKnown(string? value)
    {
        var knownValue = value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(knownValue)) return knownValue;
        const string PortugueseRemaining = "Conectado - quota semanal restante ";
        const string EnglishRemaining = "Connected - weekly quota remaining ";
        if (knownValue.StartsWith(PortugueseRemaining, StringComparison.OrdinalIgnoreCase))
            return Format("ConnectedWeeklyRemaining", knownValue.Substring(PortugueseRemaining.Length));
        if (knownValue.StartsWith(EnglishRemaining, StringComparison.OrdinalIgnoreCase))
            return Format("ConnectedWeeklyRemaining", knownValue.Substring(EnglishRemaining.Length));
        return KnownTextKeys.TryGetValue(knownValue, out var key) ? Text(key) : knownValue;
    }

    private static IReadOnlyDictionary<string, string> CurrentStrings =>
        string.Equals(_languageCode, EnglishLanguageCode, StringComparison.Ordinal) ? English : Portuguese;
}
