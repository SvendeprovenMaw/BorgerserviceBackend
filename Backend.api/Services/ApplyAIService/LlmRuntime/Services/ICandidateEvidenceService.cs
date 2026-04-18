using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface ICandidateEvidenceService
{
    Task<StructuredJsonGenerationResult> GenerateCandidateEvidenceAsync(
        CandidateEvidenceGenerationRequest request,
        CancellationToken cancellationToken = default);
}