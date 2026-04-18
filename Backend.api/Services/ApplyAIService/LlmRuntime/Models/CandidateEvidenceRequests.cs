namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class CandidateEvidenceGenerationRequest
{
    public string RequirementsDocumentId { get; init; } = string.Empty;

    public string RequirementsDocumentJson { get; init; } = string.Empty;

    public List<string> CandidateFilePaths { get; init; } = [];
}