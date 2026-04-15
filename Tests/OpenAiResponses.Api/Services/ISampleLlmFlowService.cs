using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Exposes the sample LLM pipeline as individually callable stages plus end-to-end orchestration helpers.
/// </summary>
public interface ISampleLlmFlowService
{
    /// <summary>
    /// Builds company context for the default sample job listing and applicant profile.
    /// </summary>
    Task<string> RunCompanyContextAsync(CancellationToken cancellationToken = default);

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
    /// Runs the sample pipeline without verification metadata.
    /// </summary>
    Task<string> RunPipelineAsync(SamplePipelineSelectionRequest? selection = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pipeline with verification, repair, and gate information included in the response.
    /// </summary>
    Task<string> RunPipelineWithVerificationAsync(SamplePipelineSelectionRequest? selection = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Repeats the verified pipeline for every job listing in the sample corpus.
    /// </summary>
    Task<string> RunPipelineWithVerificationForAllJobListingsAsync(CancellationToken cancellationToken = default);
}