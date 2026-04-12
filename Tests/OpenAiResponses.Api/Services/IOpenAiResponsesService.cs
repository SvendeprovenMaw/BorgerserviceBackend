using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Wraps the OpenAI Responses API behind request models that are easier for the sample routes to compose.
/// </summary>
public interface IOpenAiResponsesService
{
    /// <summary>
    /// Generates strict JSON for the legacy candidate-vs-job comparison shape.
    /// </summary>
    Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates strict JSON for arbitrary prompt, text, and file combinations.
    /// </summary>
    Task<string> GenerateStructuredJsonAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default);
}
