namespace Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;

public static class CurrencyCodeHelper
{
    public static string Normalize(string? currency, string defaultCurrency = "USD")
    {
        return IsIso4217Like(currency)
            ? currency!.Trim().ToUpperInvariant()
            : defaultCurrency;
    }

    public static string NormalizeRequired(string currency, string parameterName)
    {
        if (!IsIso4217Like(currency))
        {
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code.", parameterName);
        }

        return currency.Trim().ToUpperInvariant();
    }

    public static bool IsIso4217Like(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return false;
        }

        var trimmed = currency.Trim();
        return trimmed.Length == 3 && trimmed.All(char.IsLetter);
    }
}