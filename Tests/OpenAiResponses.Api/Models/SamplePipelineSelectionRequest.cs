namespace OpenAiResponses.Api.Models;

/// <summary>
/// Optional sample-data selectors for the end-to-end sample pipeline routes.
/// </summary>
public sealed class SamplePipelineSelectionRequest
{
    /// <summary>
    /// Optional 1-based candidate selector.
    /// The current sample corpus exposes 1 for Borger1 and 2 for Borger2.
    /// </summary>
    public int? CandidateNumber { get; init; }

    /// <summary>
    /// Optional 1-based job-posting selector.
    /// The current sample corpus exposes 1 through 5 in the sorted TestData/Opslag directory.
    /// </summary>
    public int? JobPostingNumber { get; init; }
}