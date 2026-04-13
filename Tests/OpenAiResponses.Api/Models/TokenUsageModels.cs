namespace OpenAiResponses.Api.Models;

/// <summary>
/// Structured JSON output plus metadata returned from one Responses API call.
/// </summary>
public sealed class StructuredJsonGenerationResult
{
    /// <summary>
    /// JSON text emitted by the model.
    /// </summary>
    public string OutputJson { get; init; } = string.Empty;

    /// <summary>
    /// Model name used for the response.
    /// </summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// Provider response identifier when available.
    /// </summary>
    public string? ResponseId { get; init; }

    /// <summary>
    /// Token usage reported by the provider.
    /// </summary>
    public LlmTokenUsage TokenUsage { get; init; } = new();
}

/// <summary>
/// Normalized token counters for one model interaction.
/// </summary>
public sealed class LlmTokenUsage
{
    /// <summary>
    /// Input or prompt-side tokens.
    /// </summary>
    public long InputTokens { get; init; }

    /// <summary>
    /// Output or completion-side tokens.
    /// </summary>
    public long OutputTokens { get; init; }

    /// <summary>
    /// Total billed or reported tokens for the interaction.
    /// </summary>
    public long TotalTokens { get; init; }

    /// <summary>
    /// Cached input tokens when the provider exposes them.
    /// </summary>
    public long CachedInputTokens { get; init; }

    /// <summary>
    /// Reasoning tokens when the provider exposes them.
    /// </summary>
    public long ReasoningTokens { get; init; }
}