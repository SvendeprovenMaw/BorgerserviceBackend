using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace OpenAiResponses.Api.Models;

public sealed class StrictJsonResponseRequest
{
    [Required]
    [MinLength(1)]
    public List<string> PersonFiles { get; init; } = [];

    [Required]
    public string JobApplication { get; init; } = string.Empty;

    [Required]
    public JsonElement OutputSchema { get; init; }

    [Required]
    public string Prompt { get; init; } = string.Empty;

    public string? Model { get; init; }

    public string SchemaName { get; init; } = "strict_response";

    public string? SchemaDescription { get; init; }
}
