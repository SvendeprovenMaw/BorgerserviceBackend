namespace OpenAiResponses.Api.Models;

/// <summary>
/// Internal request contract used to generate structured company context.
/// </summary>
public sealed class CompanyContextGenerationRequest
{
    /// <summary>
    /// Optional company name supplied by the caller.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Optional uploaded or pre-extracted job posting text.
    /// </summary>
    public string? JobPostingText { get; init; }

    /// <summary>
    /// Optional path to a job posting file that should be uploaded to the model.
    /// </summary>
    public string? JobPostingFilePath { get; init; }

    /// <summary>
    /// Optional profile files that may contain applicant address and other profile data.
    /// </summary>
    public List<string> ApplicantProfileFilePaths { get; init; } = [];

    /// <summary>
    /// Optional pre-extracted profile text supplied by another service.
    /// </summary>
    public string? ApplicantProfileText { get; init; }

    /// <summary>
    /// Optional direct applicant address hint when the caller already knows it.
    /// </summary>
    public string? ApplicantAddressHint { get; init; }
}

/// <summary>
/// JSON request for standalone company-context generation without file upload.
/// </summary>
public sealed class CompanyContextDirectRequest
{
    /// <summary>
    /// Optional company name used as the primary lookup key for CompanyContext.
    /// Use this when the caller already knows the employer name and wants the phase to research that company directly.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Optional raw text extracted from the job posting.
    /// Use this when another service already parsed the job ad and wants CompanyContext to infer employer, workplace, and role context without uploading a file.
    /// </summary>
    public string? JobPostingText { get; init; }

    /// <summary>
    /// Optional raw text extracted from the applicant's profile documents.
    /// Use this when another service already parsed CV/profile files and wants CompanyContext to use that text for address lookup and applicant-side context.
    /// </summary>
    public string? ApplicantProfileText { get; init; }

    /// <summary>
    /// Optional explicit applicant address or location hint.
    /// This is mainly used to support commute-distance estimation when the address is not easy to infer from applicantProfileText.
    /// </summary>
    public string? ApplicantAddressHint { get; init; }
}

/// <summary>
/// Multipart/form-data request for standalone company-context generation from uploaded files.
/// </summary>
public sealed class CompanyContextUploadRequest
{
    /// <summary>
    /// Optional company name used as a direct employer lookup key.
    /// Supply this if the job posting does not clearly identify the employer, or if the calling system already knows the target company.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Optional raw job posting text.
    /// Use this as a text fallback when the calling system already extracted text or wants to supplement the uploaded jobPostingFile.
    /// </summary>
    public string? JobPostingText { get; init; }

    /// <summary>
    /// Optional uploaded job posting file.
    /// Upload the original PDF, DOCX, image, or other local job-ad file when CompanyContext should read the source document directly.
    /// </summary>
    public IFormFile? JobPostingFile { get; init; }

    /// <summary>
    /// Optional raw applicant profile text.
    /// Use this when profile text already exists upstream and should be included without additional file parsing.
    /// </summary>
    public string? ApplicantProfileText { get; init; }

    /// <summary>
    /// Optional uploaded applicant profile files.
    /// Add CV, profile, or similar documents that may contain address or other profile context relevant to CompanyContext.
    /// </summary>
    public List<IFormFile> ApplicantProfileFiles { get; init; } = [];

    /// <summary>
    /// Optional explicit applicant address or location hint.
    /// This is used for commute estimation when the address is missing or ambiguous in ApplicantProfileFiles or ApplicantProfileText.
    /// </summary>
    public string? ApplicantAddressHint { get; init; }
}