namespace OpenAiResponses.Api.Services;

/// <summary>
/// Exposes the sample LLM pipeline as individually callable stages plus end-to-end orchestration helpers.
/// </summary>
public interface ISampleLlmFlowService
{
    /// <summary>
    /// Parses requirements from the default sample job listing.
    /// </summary>
    Task<string> RunRequirementsParsingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts candidate evidence after requirements have been parsed.
    /// </summary>
    Task<string> RunCandidateEvidenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces requirement matching for the default sample data.
    /// </summary>
    Task<string> RunMatchingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the four-stage sample pipeline without verification metadata.
    /// </summary>
    Task<string> RunPipelineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pipeline with verification, repair, and gate information included in the response.
    /// </summary>
    Task<string> RunPipelineWithVerificationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Repeats the verified pipeline for every job listing in the sample corpus.
    /// </summary>
    Task<string> RunPipelineWithVerificationForAllJobListingsAsync(CancellationToken cancellationToken = default);
}