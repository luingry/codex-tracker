using System.Globalization;

namespace CodexTracker.Core;

public static class CurrencyPresentation
{
    private static readonly CultureInfo BrazilianPortuguese = CultureInfo.GetCultureInfo("pt-BR");

    public static string Normalize(string? currencyCode) => string.Equals(currencyCode, "USD", StringComparison.OrdinalIgnoreCase) ? "USD" : "BRL";

    public static string FormatCost(decimal usd, decimal brl, string? currencyCode)
    {
        return Normalize(currencyCode) == "USD"
            ? $"US$ {usd.ToString("N2", BrazilianPortuguese)}"
            : $"R$ {brl.ToString("N2", BrazilianPortuguese)}";
    }

    public static string FormatMonthlyCost(decimal monthUsd, decimal monthBrl, string? currencyCode) => FormatCost(monthUsd, monthBrl, currencyCode);
}
