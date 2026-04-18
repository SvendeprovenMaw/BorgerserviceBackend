namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class CompanyContextGenerationRequest
{
    public string? CompanyName { get; init; }

    public string? JobPostingText { get; init; }

    public string? JobPostingFilePath { get; init; }

    public List<string> ApplicantProfileFilePaths { get; init; } = [];

    public string? ApplicantProfileText { get; init; }

    public string? ApplicantAddressHint { get; init; }
}