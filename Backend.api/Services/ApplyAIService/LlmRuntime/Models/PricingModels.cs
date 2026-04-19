namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed record CurrencyExchangeRateQuote
{
    public const string OnlineAvailability = "Online";
    public const string OfflineAvailability = "Offline";
    public const string NotRequiredAvailability = "NotRequired";

    public string ApiAvailability { get; init; } = NotRequiredAvailability;

    public string SourceCurrency { get; init; } = string.Empty;

    public string TargetCurrency { get; init; } = string.Empty;

    public decimal? AppliedRate { get; init; }

    public bool UsingStaleRate { get; init; }

    public DateTimeOffset? ProviderLastUpdateUtc { get; init; }

    public DateTimeOffset? ProviderNextUpdateUtc { get; init; }

    public DateTimeOffset? LastSuccessfulRefreshUtc { get; init; }
}

public sealed record LlmPricingSnapshot
{
    public string Model { get; init; } = string.Empty;

    public string Currency { get; init; } = "USD";

    public decimal? InputCostPerMillionTokens { get; init; }

    public decimal? CachedInputCostPerMillionTokens { get; init; }

    public decimal? OutputCostPerMillionTokens { get; init; }

    public bool PricingConfigured { get; init; }
}

public sealed record LlmTokenCostSummary
{
    public string Currency { get; init; } = "USD";

    public bool PricingConfigured { get; init; }

    public decimal? InputCost { get; init; }

    public decimal? OutputCost { get; init; }

    public decimal? TotalCost { get; init; }

    public string? RoundedUpTotalCost { get; init; }

    public decimal? RoundedUpTotalCostNumeric { get; init; }
}