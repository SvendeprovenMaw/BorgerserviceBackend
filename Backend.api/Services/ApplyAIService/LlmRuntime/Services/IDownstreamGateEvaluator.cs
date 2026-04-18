using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IDownstreamGateEvaluator
{
    GateEvaluationResult Evaluate(StageVerificationRequest request, StageVerificationResult verificationResult);
}