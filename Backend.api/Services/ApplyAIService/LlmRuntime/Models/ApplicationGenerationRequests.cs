namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class ApplicationGenerationRequest
{
    public string ApplicationDocumentId { get; init; } = string.Empty;

    public string RequirementsDocumentId { get; init; } = string.Empty;

    public string RequirementsDocumentJson { get; init; } = string.Empty;

    public string CandidateEvidenceDocumentId { get; init; } = string.Empty;

    public string CandidateEvidenceDocumentJson { get; init; } = string.Empty;

    public string CompanyContextDocumentId { get; init; } = string.Empty;

    public string CompanyContextDocumentJson { get; init; } = string.Empty;

    public string MatchingDocumentId { get; init; } = string.Empty;

    public string MatchingDocumentJson { get; init; } = string.Empty;

    public string PreferencesJson { get; init; } = string.Empty;
}