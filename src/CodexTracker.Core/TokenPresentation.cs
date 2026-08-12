using System.Globalization;

namespace CodexTracker.Core;

public static class TokenPresentation
{
    private static readonly CultureInfo BrazilianPortuguese = CultureInfo.GetCultureInfo("pt-BR");

    public static string Format(long tokens)
    {
        if (tokens < 1_000) return tokens.ToString("N0", BrazilianPortuguese);
        if (tokens < 1_000_000) return FormatScaled(tokens, 1_000m, "mil");
        if (tokens < 1_000_000_000) return FormatScaled(tokens, 1_000_000m, "mi");
        return FormatScaled(tokens, 1_000_000_000m, "bi");
    }

    private static string FormatScaled(long tokens, decimal divisor, string suffix) => $"{(tokens / divisor).ToString("0.##", BrazilianPortuguese)} {suffix}";
}
