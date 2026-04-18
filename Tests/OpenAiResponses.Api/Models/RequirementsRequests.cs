using Microsoft.AspNetCore.Http;

namespace OpenAiResponses.Api.Models;

/// <summary>
/// Internal request contract used to generate structured requirements from one job posting.
/// </summary>
public sealed class RequirementsGenerationRequest
{
    /// <summary>
    /// Optional raw job posting text supplied by the caller.
    /// </summary>
    public string? JobPostingText { get; init; }

    /// <summary>
    /// Optional path to a job posting file that should be uploaded to the model.
    /// </summary>
    public string? JobPostingFilePath { get; init; }
}

/// <summary>
/// Multipart/form-data request for standalone requirements extraction from an uploaded job posting.
/// </summary>
public sealed class RequirementsUploadRequest
{
    /// <summary>
    /// Optional raw job posting text used when the caller already extracted the posting upstream.
    /// </summary>
    public string? JobPostingText { get; init; }

    /// <summary>
    /// Optional uploaded job posting file that should be parsed directly.
    /// </summary>
    public IFormFile? JobPostingFile { get; init; }
}

/// <summary>
/// Direct JSON request for verifying one requirements document against the production schema and gate rules.
/// </summary>
public sealed class RequirementsVerificationDirectRequest
{
    /// <summary>
    /// Stable identifier expected for the verified requirements document.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Raw requirements JSON that should be verified.
    /// </summary>
    public string DocumentJson { get; init; } = string.Empty;

    /// <summary>
    /// Job-posting filename that must appear in parsed_files metadata and citations.
    /// </summary>
    public string JobPostingFileName { get; init; } = string.Empty;
}