using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class StructuredJsonResponseRequest
{
    [Required]
    public string Prompt { get; init; } = string.Empty;

    [Required]
    public JsonElement OutputSchema { get; init; }

    public string? Model { get; init; }

    public decimal? InputCostPerMillionTokens { get; init; }

    public decimal? CachedInputCostPerMillionTokens { get; init; }

    public decimal? OutputCostPerMillionTokens { get; init; }

    public string SchemaName { get; init; } = "strict_response";

    public string? SchemaDescription { get; init; }

    public List<StructuredTextInput> InputTexts { get; init; } = [];

    public List<StructuredFileInput> InputFiles { get; init; } = [];

    public bool EnableWebSearch { get; init; }

    public bool ForceWebSearchTool { get; init; }
}

public sealed class StructuredTextInput
{
    public string Label { get; init; } = string.Empty;

    [Required]
    public string Content { get; init; } = string.Empty;
}

public sealed class StructuredFileInput
{
    public string Label { get; init; } = string.Empty;

    [Required]
    public string FilePath { get; init; } = string.Empty;
}