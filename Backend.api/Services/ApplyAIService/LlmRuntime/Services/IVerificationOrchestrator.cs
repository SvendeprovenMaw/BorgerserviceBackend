using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IVerificationOrchestrator
{
    Task<StageVerificationResult> VerifyStageAsync(StageVerificationRequest request, CancellationToken cancellationToken = default);
}