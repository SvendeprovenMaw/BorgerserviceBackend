using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IRequirementsParsingService
{
    Task<StructuredJsonGenerationResult> GenerateRequirementsAsync(RequirementsGenerationRequest request, CancellationToken cancellationToken = default);
}