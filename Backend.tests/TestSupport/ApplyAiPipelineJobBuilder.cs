using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;

namespace Backend.tests.TestSupport;

internal sealed class ApplyAiPipelineJobBuilder
{
    private readonly ApplyAiPipelineJob _job;

    public ApplyAiPipelineJobBuilder(User? user = null)
    {
        var resolvedUser = user ?? ApplyAiTestData.CreateUser();
        var createdAtUtc = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

        _job = new ApplyAiPipelineJob
        {
            Id = Guid.NewGuid(),
            UserId = resolvedUser.Id,
            User = resolvedUser,
            WorkflowMode = PipelineWorkflowMode.Auto,
            Status = PipelineJobStatus.Queued,
            CurrentActivity = PipelineActivity.Queued,
            StatusMessage = "Job accepted. Waiting for execution.",
            ProgressPercent = 0,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc,
            CorrelationId = "test-correlation-id",
            JobPostingSourceType = PipelineInputKind.UploadedFile,
            JobPostingReference = "upload://job-posting.pdf",
            JobPostingOriginalFileName = "job-posting.pdf",
            JobPostingContentType = "application/pdf",
            RequestedArtifactsJson = ApplyAiTestData.Json(new ApplyAiRequestedArtifacts()),
            PreferencesSnapshotJson = ApplyAiTestData.Json(new { applicant_display_name = "Test Applicant" }),
            SelectedFileIdsJson = "[]",
            CandidateFileSnapshotJson = "[]",
            RunStoragePrefix = StoragePathBuilder.BuildRunStoragePrefix(resolvedUser.Id, createdAtUtc, Guid.Empty),
            DisplayRunName = "ApplyAI Test Run",
        };

        foreach (var definition in PipelinePhaseCatalog.All)
        {
            _job.PhaseStates.Add(new ApplyAiPipelinePhaseState
            {
                Id = Guid.NewGuid(),
                JobId = _job.Id,
                Job = _job,
                Phase = definition.Phase,
                Status = PipelinePhaseStatus.Pending,
                StatusMessage = $"{definition.DisplayName}: pending.",
            });
        }
    }

    public ApplyAiPipelineJobBuilder WithId(Guid jobId)
    {
        _job.Id = jobId;
        foreach (var phaseState in _job.PhaseStates)
        {
            phaseState.JobId = jobId;
        }

        foreach (var artifact in _job.Artifacts)
        {
            artifact.JobId = jobId;
        }

        foreach (var pipelineEvent in _job.Events)
        {
            pipelineEvent.JobId = jobId;
        }

        return this;
    }

    public ApplyAiPipelineJobBuilder WithCreatedAt(DateTimeOffset createdAtUtc)
    {
        _job.CreatedAtUtc = createdAtUtc;
        _job.UpdatedAtUtc = createdAtUtc;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithWorkflowMode(PipelineWorkflowMode workflowMode)
    {
        _job.WorkflowMode = workflowMode;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithStatus(
        PipelineJobStatus status,
        PipelinePhase? currentPhase = null,
        PipelineActivity currentActivity = PipelineActivity.Queued,
        string statusMessage = "Job status updated.")
    {
        _job.Status = status;
        _job.CurrentPhase = currentPhase;
        _job.CurrentActivity = currentActivity;
        _job.StatusMessage = statusMessage;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithJobPosting(PipelineInputKind sourceType, string reference, string? fileName = null, string? contentType = "application/pdf")
    {
        _job.JobPostingSourceType = sourceType;
        _job.JobPostingReference = reference;
        _job.JobPostingOriginalFileName = fileName;
        _job.JobPostingContentType = contentType;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithPreferences(string? preferencesJson)
    {
        _job.PreferencesSnapshotJson = preferencesJson;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithRequestedArtifacts(ApplyAiRequestedArtifacts requestedArtifacts)
    {
        _job.RequestedArtifactsJson = ApplyAiTestData.Json(requestedArtifacts);
        return this;
    }

    public ApplyAiPipelineJobBuilder WithCandidateFiles(params ApplyAiCandidateFileSummary[] candidateFiles)
    {
        _job.SelectedFileIdsJson = JsonSerializer.Serialize(candidateFiles.Select(file => file.FileId).ToArray());
        _job.CandidateFileSnapshotJson = JsonSerializer.Serialize(candidateFiles);
        return this;
    }

    public ApplyAiPipelineJobBuilder WithPhaseState(
        PipelinePhase phase,
        PipelinePhaseStatus status,
        string? documentId = null,
        string? documentJson = null,
        string? verificationJson = null,
        string? gateJson = null,
        bool approvedForDownstream = false,
        bool approvalRequired = false,
        bool hasUnverifiedEdits = false,
        int attemptCount = 1,
        int repairAttemptCount = 0,
        string? statusMessage = null)
    {
        var phaseState = _job.PhaseStates.Single(item => item.Phase == phase);
        phaseState.Status = status;
        phaseState.DocumentId = documentId;
        phaseState.DocumentJson = documentJson;
        phaseState.VerificationJson = verificationJson;
        phaseState.GateJson = gateJson;
        phaseState.ApprovedForDownstream = approvedForDownstream;
        phaseState.ApprovalRequired = approvalRequired;
        phaseState.HasUnverifiedEdits = hasUnverifiedEdits;
        phaseState.AttemptCount = attemptCount;
        phaseState.RepairAttemptCount = repairAttemptCount;
        phaseState.StatusMessage = statusMessage ?? phaseState.StatusMessage;
        phaseState.CurrentActivity = status switch
        {
            PipelinePhaseStatus.Pending => null,
            PipelinePhaseStatus.Running => PipelineActivity.PreparingPrompt,
            PipelinePhaseStatus.AwaitingApproval => PipelineActivity.AwaitingUserApproval,
            PipelinePhaseStatus.Completed => PipelineActivity.Completed,
            PipelinePhaseStatus.Failed => PipelineActivity.Failed,
            _ => null,
        };
        phaseState.CompletedAtUtc = status is PipelinePhaseStatus.Completed or PipelinePhaseStatus.AwaitingApproval or PipelinePhaseStatus.Failed
            ? _job.CreatedAtUtc.AddMinutes(phaseState.AttemptCount)
            : null;
        return this;
    }

    public ApplyAiPipelineJobBuilder WithCompletedPhaseDocument(
        PipelinePhase phase,
        object? phaseOutput = null,
        string? documentId = null,
        bool approvedForDownstream = true,
        string? verificationJson = null,
        string? gateJson = null)
    {
        return WithPhaseState(
            phase,
            PipelinePhaseStatus.Completed,
            documentId ?? $"{_job.Id:N}:{PipelinePhaseCatalog.ToRouteSegment(phase)}:attempt-1",
            ApplyAiTestData.WrapPhaseDocument(phaseOutput ?? new { phase = phase.ToString() }),
            verificationJson ?? ApplyAiTestData.Json(new { status = approvedForDownstream ? "pass" : "blocked" }),
            gateJson ?? ApplyAiTestData.Json(new { approvedForDownstream }),
            approvedForDownstream,
            approvalRequired: false,
            hasUnverifiedEdits: false,
            attemptCount: 1,
            repairAttemptCount: 0,
            statusMessage: $"{PipelinePhaseCatalog.Get(phase).DisplayName}: completed.");
    }

    public ApplyAiPipelineJobBuilder WithStoredJobPostingArtifact(
        string displayName = "job-posting.pdf",
        string? storageKey = null,
        string mediaType = "application/pdf")
    {
        _job.Artifacts.Add(new ApplyAiPipelineArtifact
        {
            Id = Guid.NewGuid(),
            JobId = _job.Id,
            Job = _job,
            Phase = null,
            ArtifactKind = PipelineArtifactKind.PdfDocument,
            RelativePath = "inputs/job_listing/job-posting.pdf",
            StorageKey = storageKey ?? $"users/{_job.UserId:N}/Runs/2026-04-19/{_job.Id:N}/inputs/job_listing/job-posting.pdf",
            DisplayName = displayName,
            MediaType = mediaType,
            IsPrimary = true,
        });
        return this;
    }

    public ApplyAiPipelineJobBuilder WithArtifact(
        PipelineArtifactKind artifactKind,
        string displayName,
        string relativePath,
        PipelinePhase? phase = null,
        bool isPrimary = false,
        string mediaType = "application/json",
        string? storageKey = null)
    {
        _job.Artifacts.Add(new ApplyAiPipelineArtifact
        {
            Id = Guid.NewGuid(),
            JobId = _job.Id,
            Job = _job,
            Phase = phase,
            ArtifactKind = artifactKind,
            RelativePath = relativePath,
            StorageKey = storageKey,
            DisplayName = displayName,
            MediaType = mediaType,
            IsPrimary = isPrimary,
        });
        return this;
    }

    public ApplyAiPipelineJobBuilder WithEvent(
        PipelineEventType eventType,
        DateTimeOffset occurredAtUtc,
        string eventId,
        string message,
        PipelinePhase? phase = null,
        PipelineActivity activity = PipelineActivity.Queued)
    {
        _job.Events.Add(new ApplyAiPipelineEvent
        {
            Id = Guid.NewGuid(),
            JobId = _job.Id,
            Job = _job,
            EventId = eventId,
            EventType = eventType,
            JobStatus = _job.Status,
            Phase = phase,
            Activity = activity,
            ProgressPercent = _job.ProgressPercent,
            Message = message,
            OccurredAtUtc = occurredAtUtc,
        });
        return this;
    }

    public ApplyAiPipelineJob Build()
    {
        return _job;
    }
}