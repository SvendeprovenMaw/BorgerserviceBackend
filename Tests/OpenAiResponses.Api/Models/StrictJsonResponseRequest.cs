using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace OpenAiResponses.Api.Models;

/// <summary>
/// Legacy request shape for the strict-json endpoint that compares candidate files to one job posting.
/// </summary>
public sealed class StrictJsonResponseRequest
{
    /// <summary>
    /// Candidate files that should be compared against the job application.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<string> PersonFiles { get; init; } = [];

    /// <summary>
    /// The job posting file to compare the candidate against.
    /// </summary>
    [Required]
    public string JobApplication { get; init; } = string.Empty;

    /// <summary>
    /// JSON schema the response must satisfy.
    /// </summary>
    [Required]
    public JsonElement OutputSchema { get; init; }

    /// <summary>
    /// Natural-language instruction sent to the model.
    /// </summary>
    [Required]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// Optional model override for this request.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// Name used when registering the response schema with the API.
    /// </summary>
    public string SchemaName { get; init; } = "strict_response";

    /// <summary>
    /// Optional human-readable description of the schema.
    /// </summary>
    public string? SchemaDescription { get; init; }
}
