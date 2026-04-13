namespace OpenAiResponses.Api.Options;

/// <summary>
/// Configuration used when creating Responses API calls.
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-5-mini";

    public string PricingCurrency { get; set; } = "USD";

    public decimal? InputCostPerMillionTokens { get; set; }

    public decimal? OutputCostPerMillionTokens { get; set; }
}
