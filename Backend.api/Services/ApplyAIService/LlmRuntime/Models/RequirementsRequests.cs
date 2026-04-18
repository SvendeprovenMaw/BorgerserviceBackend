namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class RequirementsGenerationRequest
{
    public string? JobPostingText { get; init; }

    public string? JobPostingFilePath { get; init; }
}

public sealed class RequirementsVerificationDirectRequest
{
    public string DocumentId { get; init; } = string.Empty;

    public string DocumentJson { get; init; } = string.Empty;

    public string JobPostingFileName { get; init; } = string.Empty;
}