using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace OpenAiResponses.Api.Models;

/// <summary>
/// Legacy request shape for the strict-json endpoint that compares candidate files to one job posting.
/// </summary>
public sealed class StrictJsonResponseRequest
{
    /// <summary>
    /// Local file paths to the candidate documents that should be uploaded to the model.
    /// Add CV, profile, certificates, references, or other applicant files that the model may use as source material.
    /// The route uses these files as the complete candidate-side context for the run.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> PersonFiles { get; init; } = [];

    /// <summary>
    /// Local file path to the job posting that defines the target role.
    /// This file is uploaded alongside PersonFiles and is used as the only job-side source for the comparison.
    /// </summary>
    [Required]
    public string JobApplication { get; init; } = string.Empty;

    /// <summary>
    /// JSON Schema object that the model output must match exactly.
    /// This must be the raw schema object itself, not a wrapper document with name/strict metadata.
    /// </summary>
    [Required]
    public JsonElement OutputSchema { get; init; }

    /// <summary>
    /// Task instruction for the model.
    /// Use this to describe what the model should do with the uploaded candidate files and the uploaded job posting.
    /// </summary>
    [Required]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// Optional explicit model id override.
    /// Leave empty to use the API default model configuration from appsettings.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Stable schema name sent to the Responses API when registering the schema.
    /// Use a short machine-friendly name so technical consumers can identify the contract in logs and traces.
    /// </summary>
    public string SchemaName { get; init; } = "strict_response";

    /// <summary>
    /// Optional human-readable schema description.
    /// This helps technicians understand the intended output contract, but it does not replace OutputSchema.
    /// </summary>
    public string? SchemaDescription { get; init; }
}
