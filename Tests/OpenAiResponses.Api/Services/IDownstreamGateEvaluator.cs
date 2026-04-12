using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Converts verification findings into a continue, advisory, or repair/regenerate decision for the next stage.
/// </summary>
public interface IDownstreamGateEvaluator
{
    /// <summary>
    /// Evaluates one verified stage output against the configured gate policy.
    /// </summary>
    GateEvaluationResult Evaluate(StageVerificationRequest request, StageVerificationResult verificationResult);
}