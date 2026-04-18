using ApplyAI.LlmPipeline;
using Backend.api.Entities;

namespace Backend.api.Services.ApplyAIService
{
    public sealed class ApplyAiPipelineJob
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public User User { get; set; } = null!;
        public PipelineWorkflowMode WorkflowMode { get; set; }
        public PipelineJobStatus Status { get; set; }
        public PipelinePhase? CurrentPhase { get; set; }
        public PipelineActivity CurrentActivity { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public int ProgressPercent { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public string? CorrelationId { get; set; }
        public PipelineInputKind JobPostingSourceType { get; set; }
        public string JobPostingReference { get; set; } = string.Empty;
        public string? JobPostingOriginalFileName { get; set; }
        public string? JobPostingContentType { get; set; }
        public bool IncludeCurrentCv { get; set; }
        public bool IncludeProfileRelevantDocuments { get; set; }
        public bool IncludeAllConsentedFiles { get; set; }
        public string SelectedFileIdsJson { get; set; } = "[]";
        public string CandidateFileSnapshotJson { get; set; } = "[]";
        public string RequestedArtifactsJson { get; set; } = "{}";
        public string? PreferencesSnapshotJson { get; set; }
        public string? CompanyNameOverride { get; set; }
        public string? ApplicantAddressHint { get; set; }
        public string? RunStoragePrefix { get; set; }
        public string DisplayRunName { get; set; } = string.Empty;
        public ICollection<ApplyAiPipelinePhaseState> PhaseStates { get; set; } = [];
        public ICollection<ApplyAiPipelineArtifact> Artifacts { get; set; } = [];
        public ICollection<ApplyAiPipelineEvent> Events { get; set; } = [];
    }

    public sealed class ApplyAiPipelinePhaseState
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public ApplyAiPipelineJob Job { get; set; } = null!;
        public PipelinePhase Phase { get; set; }
        public PipelinePhaseStatus Status { get; set; }
        public PipelineActivity? CurrentActivity { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public int RepairAttemptCount { get; set; }
        public int WarningCount { get; set; }
        public int ErrorCount { get; set; }
        public bool ApprovalRequired { get; set; }
        public bool ApprovedForDownstream { get; set; }
        public bool HasUnverifiedEdits { get; set; }
        public DateTimeOffset? StartedAtUtc { get; set; }
        public DateTimeOffset? CompletedAtUtc { get; set; }
        public DateTimeOffset? ApprovedAtUtc { get; set; }
        public string? DocumentId { get; set; }
        public string? DocumentJson { get; set; }
        public string? VerificationJson { get; set; }
        public string? GateJson { get; set; }
    }

    public sealed class ApplyAiPipelineArtifact
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public ApplyAiPipelineJob Job { get; set; } = null!;
        public PipelinePhase? Phase { get; set; }
        public PipelineArtifactKind ArtifactKind { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public string? StorageKey { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string MediaType { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
    }

    public sealed class ApplyAiPipelineEvent
    {
        public Guid Id { get; set; }
        public Guid JobId { get; set; }
        public ApplyAiPipelineJob Job { get; set; } = null!;
        public string EventId { get; set; } = string.Empty;
        public PipelineEventType EventType { get; set; }
        public PipelineJobStatus JobStatus { get; set; }
        public PipelinePhase? Phase { get; set; }
        public PipelineActivity Activity { get; set; }
        public int ProgressPercent { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTimeOffset OccurredAtUtc { get; set; }
    }

    public sealed record ApplyAiResolvedUserContext(
        Guid UserId,
        Guid? ProfileId,
        ApplyAiCandidateFileSummary[] CandidateFiles,
        bool HasAcceptedActiveTerms);

    public sealed record ApplyAiCandidateFileSummary(Guid FileId, string FileName, DateTime UploadTimeUtc);
}