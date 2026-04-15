using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace OpenAiResponses.Api.Models;

/// <summary>
/// Generic request model for schema-constrained OpenAI runs that mix text inputs and files.
/// </summary>
public sealed class StructuredJsonResponseRequest
{
    /// <summary>
    /// Natural-language instruction that tells the model what to produce.
    /// </summary>
    [Required]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// JSON schema the model output must match exactly.
    /// </summary>
    [Required]
    public JsonElement OutputSchema { get; init; }

    /// <summary>
    /// Optional model override for this request.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Name used when registering the schema with the Responses API.
    /// </summary>
    public string SchemaName { get; init; } = "strict_response";

    /// <summary>
    /// Optional description passed alongside the schema.
    /// </summary>
    public string? SchemaDescription { get; init; }

    /// <summary>
    /// Inline text inputs appended to the prompt context.
    /// </summary>
    public List<StructuredTextInput> InputTexts { get; init; } = [];

    /// <summary>
    /// File inputs uploaded alongside the prompt context.
    /// </summary>
    public List<StructuredFileInput> InputFiles { get; init; } = [];

    /// <summary>
    /// Enables the Responses API web search tool for this request.
    /// </summary>
    public bool EnableWebSearch { get; init; }

    /// <summary>
    /// Forces the model to use the web search tool before responding.
    /// </summary>
    public bool ForceWebSearchTool { get; init; }
}

/// <summary>
/// Named text payload passed alongside the main prompt.
/// </summary>
public sealed class StructuredTextInput
{
    /// <summary>
    /// Friendly label shown to the model before the text content.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The actual text payload.
    /// </summary>
    [Required]
    public string Content { get; init; } = string.Empty;
}

/// <summary>
/// Named file payload passed to the Responses API.
/// </summary>
public sealed class StructuredFileInput
{
    /// <summary>
    /// Friendly label shown to the model before the file attachment.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Path to the local file that should be uploaded.
    /// </summary>
    [Required]
    public string FilePath { get; init; } = string.Empty;
}