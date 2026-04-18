using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using ApplyAI.LlmPipeline;
using Microsoft.AspNetCore.Http;

namespace Backend.api.Services.ApplyAIService
{
    /// <summary>
    /// JSON request body for creating an ApplyAI pipeline job.
    /// </summary>
    public sealed record ApplyAiJobRequest
    {
        /// <summary>
        /// Determines whether the pipeline continues automatically or pauses for manual review between phases.
        /// </summary>
        public PipelineWorkflowMode WorkflowMode { get; init; } = PipelineWorkflowMode.Auto;

        /// <summary>
        /// Describes where the job posting comes from, such as a remote URL or an uploaded file reference.
        /// </summary>
        public ApplyAiJobPostingSourceRequest JobPostingSource { get; init; } = new();

        /// <summary>
        /// Selects which consented user-side files should be loaded into the pipeline run.
        /// </summary>
        public ApplyAiCandidateDocumentSelection CandidateDocuments { get; init; } = new();

        /// <summary>
        /// Optional company-context enrichment hints supplied by the caller.
        /// </summary>
        public ApplyAiCompanyContextOverrides CompanyContextOverrides { get; init; } = new();

        /// <summary>
        /// Raw JSON preferences used by the application-generation phase.
        /// </summary>
        public JsonElement? PreferencesOverride { get; init; }

        /// <summary>
        /// Selects which high-level artifacts the current ApplyAI implementation should expose.
        /// </summary>
        public ApplyAiRequestedArtifacts RequestedArtifacts { get; init; } = new();

        /// <summary>
        /// Optional caller-generated identifier used to correlate frontend requests with pipeline jobs.
        /// </summary>
        public string? CorrelationId { get; init; }
    }

    /// <summary>
    /// Describes the source of the job posting used by a pipeline run.
    /// </summary>
    public sealed record ApplyAiJobPostingSourceRequest
    {
        /// <summary>
        /// Declares the job-posting source type, for example remote URL or uploaded file.
        /// </summary>
        public PipelineInputKind SourceType { get; init; } = PipelineInputKind.RemoteUrl;

        /// <summary>
        /// Internal source reference used by the backend when the caller does not pass the public `url` field.
        /// </summary>
        public string? Reference { get; init; }

        /// <summary>
        /// Public URL value used when the job posting comes from a remote location.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; init; }

        /// <summary>
        /// Optional original file name when the source is file-based.
        /// </summary>
        public string? FileName { get; init; }

        /// <summary>
        /// Optional content type supplied for file-based sources.
        /// </summary>
        public string? ContentType { get; init; }

        public string ResolveReference()
        {
            return !string.IsNullOrWhiteSpace(Reference)
                ? Reference
                : Url ?? string.Empty;
        }
    }

    /// <summary>
    /// Selects which authenticated-user documents should be included in a pipeline run.
    /// </summary>
    public sealed record ApplyAiCandidateDocumentSelection
    {
        /// <summary>
        /// Includes the current CV referenced by the user's profile.
        /// </summary>
        public bool IncludeCurrentCv { get; init; } = true;

        /// <summary>
        /// Includes relevant documents already attached to the user's profile.
        /// </summary>
        public bool IncludeProfileRelevantDocuments { get; init; } = true;

        /// <summary>
        /// Explicit extra file ids to include in addition to the profile defaults.
        /// </summary>
        public Guid[] AdditionalFileIds { get; init; } = [];

        /// <summary>
        /// Expands the evidence set to all consented files owned by the authenticated user.
        /// </summary>
        public bool IncludeAllConsentedFiles { get; init; }
    }

    /// <summary>
    /// Optional hints that help the company-context phase.
    /// </summary>
    public sealed record ApplyAiCompanyContextOverrides
    {
        /// <summary>
        /// Explicit company name override used when the caller already knows which employer should be targeted.
        /// </summary>
        public string? CompanyName { get; init; }

        /// <summary>
        /// Optional applicant-address hint used for commute or location reasoning.
        /// </summary>
        public string? ApplicantAddressHint { get; init; }
    }

    /// <summary>
    /// High-level artifact-selection flags for the current ApplyAI implementation.
    /// </summary>
    public sealed record ApplyAiRequestedArtifacts
    {
        /// <summary>
        /// Includes the fit-advisory artifact in the saved output set.
        /// </summary>
        public bool IncludeFitAdvisory { get; init; } = true;

        /// <summary>
        /// Includes the current cover-letter output set produced by the pipeline implementation.
        /// </summary>
        public bool IncludeCoverLetter { get; init; } = true;
    }

    /// <summary>
    /// JSON request body for creating a pipeline job from a job-posting link that must be rendered to PDF server-side.
    /// </summary>
    public sealed record ApplyAiJobLinkRequest
    {
        /// <summary>
        /// Job-posting page URL that should be rendered to PDF through the backend Playwright service.
        /// </summary>
        [Required]
        public string Url { get; init; } = string.Empty;

        /// <summary>
        /// Determines whether the pipeline continues automatically or pauses for manual review.
        /// </summary>
        public PipelineWorkflowMode WorkflowMode { get; init; } = PipelineWorkflowMode.Auto;

        /// <summary>
        /// Includes the current CV referenced by the authenticated user's profile.
        /// </summary>
        public bool IncludeCurrentCv { get; init; } = true;

        /// <summary>
        /// Includes relevant profile documents already attached to the authenticated user.
        /// </summary>
        public bool IncludeProfileRelevantDocuments { get; init; } = true;

        /// <summary>
        /// Explicit extra file ids that should be added to the candidate-document set.
        /// </summary>
        public Guid[] AdditionalFileIds { get; init; } = [];

        /// <summary>
        /// Includes all consented files owned by the authenticated user.
        /// </summary>
        public bool IncludeAllConsentedFiles { get; init; }

        /// <summary>
        /// Optional company-name override used by the company-context phase.
        /// </summary>
        public string? CompanyName { get; init; }

        /// <summary>
        /// Optional applicant-address hint used by the company-context phase.
        /// </summary>
        public string? ApplicantAddressHint { get; init; }

        /// <summary>
        /// Preferences JSON used by the application-generation phase.
        /// </summary>
        public JsonElement? PreferencesOverride { get; init; }

        /// <summary>
        /// Selects which high-level artifacts the current ApplyAI implementation should expose.
        /// </summary>
        public ApplyAiRequestedArtifacts RequestedArtifacts { get; init; } = new();

        /// <summary>
        /// Optional caller-generated identifier used to correlate frontend actions with backend jobs.
        /// </summary>
        public string? CorrelationId { get; init; }
    }

    /// <summary>
    /// Multipart form request body for creating a pipeline job from an uploaded file.
    /// </summary>
    public sealed class ApplyAiJobUploadRequest
    {
        /// <summary>
        /// Uploaded job-posting file that should seed the pipeline run.
        /// </summary>
        [Required]
        public IFormFile JobPostingFile { get; set; } = null!;

        /// <summary>
        /// Determines whether the pipeline continues automatically or pauses for manual review.
        /// </summary>
        public PipelineWorkflowMode WorkflowMode { get; set; } = PipelineWorkflowMode.Auto;

        /// <summary>
        /// Includes the current CV referenced by the authenticated user's profile.
        /// </summary>
        public bool IncludeCurrentCv { get; set; } = true;

        /// <summary>
        /// Includes relevant profile documents already attached to the authenticated user.
        /// </summary>
        public bool IncludeProfileRelevantDocuments { get; set; } = true;

        /// <summary>
        /// Explicit extra file ids that should be added to the candidate-document set.
        /// </summary>
        public Guid[] AdditionalFileIds { get; set; } = [];

        /// <summary>
        /// Includes all consented files owned by the authenticated user.
        /// </summary>
        public bool IncludeAllConsentedFiles { get; set; }

        /// <summary>
        /// Optional company-name override used by the company-context phase.
        /// </summary>
        public string? CompanyName { get; set; }

        /// <summary>
        /// Optional applicant-address hint used by the company-context phase.
        /// </summary>
        public string? ApplicantAddressHint { get; set; }

        /// <summary>
        /// Serialized preferences JSON used by the application-generation phase.
        /// </summary>
        public string? PreferencesOverrideJson { get; set; }

        /// <summary>
        /// Serialized artifact-selection JSON that maps to <see cref="ApplyAiRequestedArtifacts"/>.
        /// </summary>
        public string? RequestedArtifactsJson { get; set; }

        /// <summary>
        /// Optional caller-generated identifier used to correlate frontend actions with backend jobs.
        /// </summary>
        public string? CorrelationId { get; set; }
    }

    /// <summary>
    /// Request body for replacing the current phase document with edited JSON.
    /// </summary>
    public sealed record ApplyAiPhaseDocumentUpdateRequest
    {
        /// <summary>
        /// Edited JSON payload that should become the new canonical phase document.
        /// </summary>
        public JsonElement DocumentJson { get; init; }

        /// <summary>
        /// Optional reviewer note explaining why the document was edited.
        /// </summary>
        public string? EditorComment { get; init; }
    }

    /// <summary>
    /// Request body for approving a phase during manual review.
    /// </summary>
    public sealed record ApplyAiPhaseApprovalRequest
    {
        /// <summary>
        /// Optional reviewer note recorded with the approval action.
        /// </summary>
        public string? Comment { get; init; }
    }

    /// <summary>
    /// Request body for retrying one phase.
    /// </summary>
    public sealed record ApplyAiPhaseRetryRequest
    {
        /// <summary>
        /// Optional operator note that explains why the retry was requested.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Optional company-context overrides applied when they are relevant to the selected phase.
        /// </summary>
        public ApplyAiCompanyContextOverrides? CompanyContextOverrides { get; init; }

        /// <summary>
        /// Optional replacement preferences JSON, mainly relevant when retrying application generation.
        /// </summary>
        public JsonElement? PreferencesOverride { get; init; }
    }

    public sealed record ApplyAiArtifactContentResponse(
        byte[] Content,
        string MediaType,
        string FileName);

    public sealed record ApplyAiStoredArtifact(
        Guid ArtifactId,
        string StorageKey,
        string RelativePath,
        string DisplayName,
        string MediaType,
        string? Checksum);

    public sealed record ApplyAiPhaseDocumentResponse(
        PipelinePhase Phase,
        string DocumentId,
        JsonElement DocumentJson,
        JsonElement Verification,
        JsonElement Gate,
        bool Editable,
        bool ApprovedForDownstream,
        bool HasUnverifiedEdits,
        PipelineArtifactReference[] Artifacts);
}