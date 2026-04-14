namespace OpenAiResponses.Api.Services;

/// <summary>
/// Describes the exchange-rate data used, or not used, for display-currency conversion.
/// </summary>
public sealed record CurrencyExchangeRateQuote(
    string ApiAvailability,
    string SourceCurrency,
    string TargetCurrency,
    decimal? AppliedRate,
    bool UsingStaleRate,
    DateTimeOffset? ProviderLastUpdateUtc,
    DateTimeOffset? ProviderNextUpdateUtc,
    DateTimeOffset? LastSuccessfulRefreshUtc)
{
    public const string OnlineAvailability = "Online";
    public const string OfflineAvailability = "Offline";
    public const string NotRequiredAvailability = "NotRequired";
}