using System.Text.Json.Serialization;

namespace ApplyAI.LlmPipeline;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineWorkflowMode
{
    Auto,
    Manual,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineInputKind
{
    UploadedFile,
    RemoteUrl,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineJobStatus
{
    Queued,
    Running,
    AwaitingUserAction,
    Completed,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelinePhase
{
    CompanyContext,
    Requirements,
    CandidateEvidence,
    Matching,
    ApplicationGeneration,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelinePhaseStatus
{
    Pending,
    Running,
    AwaitingApproval,
    Completed,
    Failed,
    Skipped,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineActivity
{
    Queued,
    HydratingUserContext,
    PreparingPrompt,
    SendingTaskToLlm,
    WaitingForLlmResponse,
    ParsingResponse,
    RunningVerification,
    RunningRepair,
    PersistingArtifacts,
    AwaitingUserApproval,
    Completed,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineActionKind
{
    ApprovePhase,
    RetryPhase,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineEventType
{
    JobAccepted,
    JobProgressUpdated,
    PhaseStarted,
    PhaseCompleted,
    ApprovalRequired,
    JobFailed,
    JobCompleted,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PipelineArtifactKind
{
    JsonDocument,
    HtmlDocument,
    PdfDocument,
    CssStylesheet,
    VerificationReport,
    GateReport,
    Advisory,
    UsageReport,
    Other,
}

public sealed record PipelineSourceReference(
    PipelineInputKind Kind,
    string Reference,
    string? FileName = null,
    string? ContentType = null);

public sealed record PipelineRequestedArtifacts(
    bool IncludeCoverLetter = true,
    bool IncludeFitAdvisory = true);

public sealed record PipelineSubmissionRequest(
    string ApplicantId,
    PipelineWorkflowMode WorkflowMode,
    PipelineSourceReference JobListingSource,
    PipelineRequestedArtifacts RequestedArtifacts,
    string? CorrelationId = null);

public sealed record PipelinePhaseApprovalRequest(
    PipelinePhase Phase,
    string[] EditedArtifactPaths,
    string? ReviewerComment = null);

public sealed record PipelineValidationIssue(string Code, string Message);

public sealed record PipelineActionLink(
    PipelineActionKind Action,
    string Label,
    string TargetUrl,
    string HttpMethod,
    PipelinePhase? Phase = null);

public sealed record PipelineArtifactReference(
    PipelineArtifactKind Kind,
    string RelativePath,
    string DisplayName,
    string MediaType,
    PipelinePhase? Phase = null,
    bool IsPrimary = false);

public sealed record PipelinePhaseDefinition(
    PipelinePhase Phase,
    string DisplayName,
    string RouteSegment,
    string PromptPath,
    string OutputSchemaPath,
    string? VerificationSchemaPath,
    bool SupportsRepair);

public sealed record PipelinePhaseSnapshot(
    PipelinePhase Phase,
    string DisplayName,
    PipelinePhaseStatus Status,
    PipelineActivity? CurrentActivity,
    string StatusMessage,
    int AttemptCount,
    int RepairAttemptCount,
    int WarningCount,
    int ErrorCount,
    bool ApprovalRequired,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    PipelineArtifactReference[] Artifacts);

public sealed record PipelineJobAcceptedResponse(
    string JobId,
    PipelineWorkflowMode WorkflowMode,
    string StatusUrl,
    string EventsUrl,
    string ApprovePhaseUrlTemplate,
    string RetryPhaseUrlTemplate,
    int RecommendedPollIntervalSeconds,
    bool SupportsServerSentEvents,
    DateTimeOffset CreatedAtUtc);

public sealed record PipelineJobSnapshot(
    string JobId,
    PipelineWorkflowMode WorkflowMode,
    PipelineJobStatus Status,
    PipelinePhase? CurrentPhase,
    PipelineActivity CurrentActivity,
    string StatusMessage,
    int ProgressPercent,
    string StatusUrl,
    string EventsUrl,
    int RecommendedPollIntervalSeconds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    PipelineActionLink[] AvailableActions,
    PipelineArtifactReference[] Artifacts,
    PipelinePhaseSnapshot[] Phases);

public sealed record PipelineEventEnvelope(
    string EventId,
    PipelineEventType EventType,
    string JobId,
    PipelineJobStatus JobStatus,
    PipelinePhase? Phase,
    PipelineActivity Activity,
    int ProgressPercent,
    string Message,
    DateTimeOffset OccurredAtUtc);