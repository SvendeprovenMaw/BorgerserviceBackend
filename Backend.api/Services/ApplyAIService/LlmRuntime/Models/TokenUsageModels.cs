namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class StructuredJsonGenerationResult
{
    public string OutputJson { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string? ResponseId { get; init; }

    public LlmTokenUsage TokenUsage { get; init; } = new();

    public string RequestedDisplayCurrency { get; init; } = string.Empty;

    public string DisplayCurrency { get; init; } = string.Empty;

    public LlmPricingSnapshot Pricing { get; init; } = new();

    public CurrencyExchangeRateQuote CurrencyExchange { get; init; } = new();

    public LlmTokenCostSummary EstimatedCost { get; init; } = new();
}

public sealed class LlmTokenUsage
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long ReasoningTokens { get; init; }
}