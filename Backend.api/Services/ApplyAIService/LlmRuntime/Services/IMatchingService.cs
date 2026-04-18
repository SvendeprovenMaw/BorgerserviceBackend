using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IMatchingService
{
    Task<StructuredJsonGenerationResult> GenerateMatchingAsync(
        MatchingGenerationRequest request,
        CancellationToken cancellationToken = default);
}