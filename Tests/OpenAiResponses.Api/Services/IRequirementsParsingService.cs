using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Runs the standalone requirements parsing phase using the production prompt and schema assets.
/// </summary>
public interface IRequirementsParsingService
{
    Task<StructuredJsonGenerationResult> GenerateRequirementsAsync(RequirementsGenerationRequest request, CancellationToken cancellationToken = default);
}