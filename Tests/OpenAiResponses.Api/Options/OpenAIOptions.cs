namespace OpenAiResponses.Api.Options;

/// <summary>
/// Configuration used when creating Responses API calls.
/// </summary>
public sealed class OpenAIOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public int Model { get; set; } = 1;

    public string PricingCurrency { get; set; } = "USD";

    public string DisplayCurrency { get; set; } = "USD";

    public decimal? InputCostPerMillionTokens { get; set; }

    public decimal? CachedInputCostPerMillionTokens { get; set; }

    public decimal? OutputCostPerMillionTokens { get; set; }

    public Dictionary<int, OpenAIModelCatalogEntry> Models { get; set; } = new();

    public OpenAIPhaseCollectionOptions Phases { get; set; } = new();

    public OpenAIModelCatalogEntry ResolveModelEntry(int? modelSelection = null)
    {
        var selectedModel = modelSelection ?? Model;

        if (!Models.TryGetValue(selectedModel, out var entry) || string.IsNullOrWhiteSpace(entry.Id))
        {
            throw new InvalidOperationException($"OpenAI model selection '{selectedModel}' is not configured.");
        }

        return entry;
    }

    public string ResolveModelId(int? modelSelection = null)
    {
        return ResolveModelEntry(modelSelection).Id.Trim();
    }
}

/// <summary>
/// One selectable OpenAI model entry exposed in configuration.
/// </summary>
public sealed class OpenAIModelCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public bool SupportsVision { get; set; } = true;

    public decimal? InputCostPerMillionTokens { get; set; }

    public decimal? CachedInputCostPerMillionTokens { get; set; }

    public decimal? OutputCostPerMillionTokens { get; set; }
}

/// <summary>
/// Per-phase overrides for model selection and cost accounting.
/// </summary>
public sealed class OpenAIPhaseCollectionOptions
{
    public OpenAIPhaseExecutionOptions CompanyContext { get; set; } = new();

    public OpenAIPhaseExecutionOptions Requirements { get; set; } = new();

    public OpenAIPhaseExecutionOptions CandidateEvidence { get; set; } = new();

    public OpenAIPhaseExecutionOptions Matching { get; set; } = new();

    public OpenAIPhaseExecutionOptions ApplicationGeneration { get; set; } = new();
}

/// <summary>
/// One phase-specific OpenAI model and pricing override.
/// </summary>
public sealed class OpenAIPhaseExecutionOptions
{
    public int? Model { get; set; }

    public decimal? InputCostPerMillionTokens { get; set; }

    public decimal? CachedInputCostPerMillionTokens { get; set; }

    public decimal? OutputCostPerMillionTokens { get; set; }
}
