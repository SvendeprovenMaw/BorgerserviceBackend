using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IApplicationGenerationService
{
    Task<StructuredJsonGenerationResult> GenerateApplicationGenerationAsync(
        ApplicationGenerationRequest request,
        CancellationToken cancellationToken = default);
}