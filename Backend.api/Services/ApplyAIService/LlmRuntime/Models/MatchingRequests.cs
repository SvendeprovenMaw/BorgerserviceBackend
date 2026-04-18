namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class MatchingGenerationRequest
{
    public string RequirementsDocumentId { get; init; } = string.Empty;

    public string RequirementsDocumentJson { get; init; } = string.Empty;

    public string CandidateEvidenceDocumentId { get; init; } = string.Empty;

    public string CandidateEvidenceDocumentJson { get; init; } = string.Empty;

    public string? RegenerationFeedbackJson { get; init; }
}