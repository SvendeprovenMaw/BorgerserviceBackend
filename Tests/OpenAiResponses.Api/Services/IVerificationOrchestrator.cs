using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Applies schema validation plus stage-specific mechanical rules to one pipeline document.
/// </summary>
public interface IVerificationOrchestrator
{
    /// <summary>
    /// Verifies one stage output and returns normalized findings that later drive the gate evaluator.
    /// </summary>
    Task<StageVerificationResult> VerifyStageAsync(StageVerificationRequest request, CancellationToken cancellationToken = default);
}