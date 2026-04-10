namespace OpenAiResponses.Api.Services;

public interface ISampleLlmFlowService
{
    Task<string> RunRequirementsParsingAsync(CancellationToken cancellationToken = default);

    Task<string> RunCandidateEvidenceAsync(CancellationToken cancellationToken = default);

    Task<string> RunMatchingAsync(CancellationToken cancellationToken = default);

    Task<string> RunPipelineAsync(CancellationToken cancellationToken = default);
}