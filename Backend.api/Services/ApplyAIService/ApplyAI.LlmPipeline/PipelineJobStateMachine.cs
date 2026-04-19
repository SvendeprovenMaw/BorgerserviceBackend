namespace ApplyAI.LlmPipeline;

public sealed class PipelineJobStateMachine
{
    private readonly string _jobId;
    private readonly string _routePrefix;
    private readonly PipelineSubmissionRequest _request;
    private readonly List<PipelineArtifactReference> _artifacts = [];
    private readonly List<PipelineEventEnvelope> _events = [];
    private readonly List<PhaseState> _phaseStates;

    private PipelineJobStatus _status;
    private PipelinePhase? _currentPhase;
    private PipelineActivity _currentActivity;
    private string _statusMessage;
    private readonly DateTimeOffset _createdAtUtc;
    private DateTimeOffset _updatedAtUtc;
    private DateTimeOffset? _completedAtUtc;

    public PipelineJobStateMachine(
        string jobId,
        PipelineSubmissionRequest request,
        DateTimeOffset createdAtUtc,
        string routePrefix = "/api/ai/pipeline/jobs")
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        PipelineSubmissionValidator.EnsureValid(request);

        _jobId = jobId;
        _request = request;
        _createdAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
        _routePrefix = NormalizeRoutePrefix(routePrefix);
        _status = PipelineJobStatus.Queued;
        _currentActivity = PipelineActivity.Queued;
        _statusMessage = "Job accepted. Waiting for execution.";
        _phaseStates = PipelinePhaseCatalog.All.Select(definition => new PhaseState(definition)).ToList();

        AppendEvent(PipelineEventType.JobAccepted, createdAtUtc);
    }

    public PipelineJobAcceptedResponse CreateAcceptedResponse()
    {
        return new PipelineJobAcceptedResponse(
            _jobId,
            _request.WorkflowMode,
            BuildStatusUrl(),
            BuildEventsUrl(),
            BuildApproveUrlTemplate(),
            BuildRetryUrlTemplate(),
            GetRecommendedPollIntervalSeconds(),
            true,
            _createdAtUtc);
    }

    public IReadOnlyList<PipelineEventEnvelope> GetEvents()
    {
        return _events.AsReadOnly();
    }

    public PipelineJobSnapshot GetSnapshot()
    {
        return new PipelineJobSnapshot(
            _jobId,
            _request.WorkflowMode,
            _status,
            _currentPhase,
            _currentActivity,
            _statusMessage,
            CalculateProgressPercent(),
            BuildStatusUrl(),
            BuildEventsUrl(),
            GetRecommendedPollIntervalSeconds(),
            _createdAtUtc,
            _updatedAtUtc,
            _completedAtUtc,
            BuildAvailableActions(),
            _artifacts.ToArray(),
            _phaseStates.Select(CreatePhaseSnapshot).ToArray());
    }

    public void UpdateJobActivity(PipelineActivity activity, DateTimeOffset timestamp, string? statusMessage = null)
    {
        _status = PipelineJobStatus.Running;
        _currentPhase = null;
        _currentActivity = activity;
        _statusMessage = statusMessage ?? BuildStatusMessage(null, activity);
        _updatedAtUtc = timestamp;

        AppendEvent(PipelineEventType.JobProgressUpdated, timestamp);
    }

    public void StartPhase(
        PipelinePhase phase,
        DateTimeOffset timestamp,
        PipelineActivity activity = PipelineActivity.PreparingPrompt,
        string? statusMessage = null)
    {
        var phaseState = GetPhaseState(phase);
        var wasPending = phaseState.Status == PipelinePhaseStatus.Pending;
        var wasFailed = phaseState.Status == PipelinePhaseStatus.Failed;

        if (phaseState.Status == PipelinePhaseStatus.Completed)
        {
            throw new InvalidOperationException($"Phase {phase} is already completed.");
        }

        if (wasPending || wasFailed)
        {
            phaseState.AttemptCount++;
        }

        if (wasFailed)
        {
            phaseState.ErrorCount = 0;
            phaseState.WarningCount = 0;
        }

        phaseState.Status = PipelinePhaseStatus.Running;
        phaseState.CurrentActivity = activity;
        phaseState.StartedAtUtc ??= timestamp;
        phaseState.CompletedAtUtc = null;
        phaseState.ApprovedAtUtc = null;
        phaseState.ApprovalRequired = false;
        phaseState.StatusMessage = statusMessage ?? BuildStatusMessage(phase, activity);

        if (activity == PipelineActivity.RunningRepair)
        {
            phaseState.RepairAttemptCount++;
        }

        _status = PipelineJobStatus.Running;
        _currentPhase = phase;
        _currentActivity = activity;
        _statusMessage = phaseState.StatusMessage;
        _updatedAtUtc = timestamp;

        AppendEvent(wasPending ? PipelineEventType.PhaseStarted : PipelineEventType.JobProgressUpdated, timestamp);
    }

    public void UpdatePhaseActivity(
        PipelinePhase phase,
        PipelineActivity activity,
        DateTimeOffset timestamp,
        string? statusMessage = null)
    {
        var phaseState = GetPhaseState(phase);
        var isFirstRepairUpdate = activity == PipelineActivity.RunningRepair && phaseState.CurrentActivity != PipelineActivity.RunningRepair;

        if (phaseState.Status == PipelinePhaseStatus.Pending)
        {
            phaseState.AttemptCount++;
            phaseState.StartedAtUtc ??= timestamp;
        }

        phaseState.Status = PipelinePhaseStatus.Running;
        phaseState.CurrentActivity = activity;
        phaseState.StatusMessage = statusMessage ?? BuildStatusMessage(phase, activity);

        if (isFirstRepairUpdate)
        {
            phaseState.RepairAttemptCount++;
        }

        _status = PipelineJobStatus.Running;
        _currentPhase = phase;
        _currentActivity = activity;
        _statusMessage = phaseState.StatusMessage;
        _updatedAtUtc = timestamp;

        AppendEvent(PipelineEventType.JobProgressUpdated, timestamp);
    }

    public void CompletePhase(
        PipelinePhase phase,
        DateTimeOffset timestamp,
        IEnumerable<PipelineArtifactReference>? artifacts = null,
        int warningCount = 0,
        int errorCount = 0,
        string? statusMessage = null)
    {
        var phaseState = GetPhaseState(phase);
        phaseState.StartedAtUtc ??= timestamp;
        phaseState.CompletedAtUtc = timestamp;
        phaseState.WarningCount = warningCount;
        phaseState.ErrorCount = errorCount;
        phaseState.CurrentActivity = PipelineActivity.Completed;

        AddArtifacts(phaseState, artifacts);

        var nextPhase = PipelinePhaseCatalog.GetNext(phase);
        var requiresApproval = _request.WorkflowMode == PipelineWorkflowMode.Manual && nextPhase.HasValue;

        if (requiresApproval)
        {
            phaseState.Status = PipelinePhaseStatus.AwaitingApproval;
            phaseState.ApprovalRequired = true;
            phaseState.StatusMessage = statusMessage ?? $"{phaseState.Definition.DisplayName}: ready for manual review.";

            _status = PipelineJobStatus.AwaitingUserAction;
            _currentPhase = phase;
            _currentActivity = PipelineActivity.AwaitingUserApproval;
            _statusMessage = phaseState.StatusMessage;
            _updatedAtUtc = timestamp;

            AppendEvent(PipelineEventType.ApprovalRequired, timestamp);
            return;
        }

        phaseState.Status = PipelinePhaseStatus.Completed;
        phaseState.ApprovalRequired = false;
        phaseState.StatusMessage = statusMessage ?? $"{phaseState.Definition.DisplayName}: completed.";

        if (nextPhase is null)
        {
            _status = PipelineJobStatus.Completed;
            _currentPhase = null;
            _currentActivity = PipelineActivity.Completed;
            _statusMessage = "Pipeline completed.";
            _updatedAtUtc = timestamp;
            _completedAtUtc = timestamp;

            AppendEvent(PipelineEventType.JobCompleted, timestamp);
            return;
        }

        _status = PipelineJobStatus.Running;
        _currentPhase = nextPhase.Value;
        _currentActivity = PipelineActivity.Queued;
        _statusMessage = statusMessage ?? $"{PipelinePhaseCatalog.Get(nextPhase.Value).DisplayName}: queued.";
        _updatedAtUtc = timestamp;

        AppendEvent(PipelineEventType.PhaseCompleted, timestamp);
    }

    public void ApprovePhase(PipelinePhase phase, DateTimeOffset timestamp, string? statusMessage = null)
    {
        var phaseState = GetPhaseState(phase);
        if (phaseState.Status != PipelinePhaseStatus.AwaitingApproval)
        {
            throw new InvalidOperationException($"Phase {phase} is not awaiting approval.");
        }

        phaseState.Status = PipelinePhaseStatus.Completed;
        phaseState.ApprovalRequired = false;
        phaseState.ApprovedAtUtc = timestamp;
        phaseState.CurrentActivity = PipelineActivity.Completed;
        phaseState.StatusMessage = "Approved for downstream execution.";

        var nextPhase = PipelinePhaseCatalog.GetNext(phase);
        if (nextPhase is null)
        {
            _status = PipelineJobStatus.Completed;
            _currentPhase = null;
            _currentActivity = PipelineActivity.Completed;
            _statusMessage = statusMessage ?? "Pipeline completed.";
            _updatedAtUtc = timestamp;
            _completedAtUtc = timestamp;

            AppendEvent(PipelineEventType.JobCompleted, timestamp);
            return;
        }

        _status = PipelineJobStatus.Running;
        _currentPhase = nextPhase.Value;
        _currentActivity = PipelineActivity.Queued;
        _statusMessage = statusMessage ?? $"{PipelinePhaseCatalog.Get(nextPhase.Value).DisplayName}: queued after manual approval.";
        _updatedAtUtc = timestamp;

        AppendEvent(PipelineEventType.JobProgressUpdated, timestamp);
    }

    public void FailPhase(PipelinePhase phase, DateTimeOffset timestamp, string failureMessage, int errorCount = 1)
    {
        var phaseState = GetPhaseState(phase);
        phaseState.Status = PipelinePhaseStatus.Failed;
        phaseState.CurrentActivity = PipelineActivity.Failed;
        phaseState.CompletedAtUtc = timestamp;
        phaseState.ErrorCount = Math.Max(errorCount, phaseState.ErrorCount);
        phaseState.StatusMessage = failureMessage;
        phaseState.ApprovalRequired = false;

        _status = PipelineJobStatus.Failed;
        _currentPhase = phase;
        _currentActivity = PipelineActivity.Failed;
        _statusMessage = failureMessage;
        _updatedAtUtc = timestamp;
        _completedAtUtc = timestamp;

        AppendEvent(PipelineEventType.JobFailed, timestamp);
    }

    private static string NormalizeRoutePrefix(string routePrefix)
    {
        if (string.IsNullOrWhiteSpace(routePrefix))
        {
            return "/api/ai/pipeline/jobs";
        }

        var normalized = routePrefix.Trim().TrimEnd('/');
        return normalized.StartsWith('/') ? normalized : $"/{normalized}";
    }

    private string BuildStatusUrl() => $"{_routePrefix}/{_jobId}";

    private string BuildEventsUrl() => $"{BuildStatusUrl()}/events";

    private string BuildApproveUrlTemplate() => $"{BuildStatusUrl()}/phases/{{phase}}/approval";

    private string BuildRetryUrlTemplate() => $"{BuildStatusUrl()}/phases/{{phase}}/retry";

    private PipelineActionLink[] BuildAvailableActions()
    {
        if (_status == PipelineJobStatus.AwaitingUserAction && _currentPhase.HasValue)
        {
            return
            [
                new PipelineActionLink(
                    PipelineActionKind.ApprovePhase,
                    "Approve and continue",
                    $"{BuildStatusUrl()}/phases/{PipelinePhaseCatalog.ToRouteSegment(_currentPhase.Value)}/approval",
                    "POST",
                    _currentPhase),
                new PipelineActionLink(
                    PipelineActionKind.RetryPhase,
                    "Retry phase",
                    $"{BuildStatusUrl()}/phases/{PipelinePhaseCatalog.ToRouteSegment(_currentPhase.Value)}/retry",
                    "POST",
                    _currentPhase),
            ];
        }

        if (_status == PipelineJobStatus.Failed && _currentPhase.HasValue)
        {
            return
            [
                new PipelineActionLink(
                    PipelineActionKind.RetryPhase,
                    "Retry phase",
                    $"{BuildStatusUrl()}/phases/{PipelinePhaseCatalog.ToRouteSegment(_currentPhase.Value)}/retry",
                    "POST",
                    _currentPhase),
            ];
        }

        return [];
    }

    private int GetRecommendedPollIntervalSeconds()
    {
        return _status switch
        {
            PipelineJobStatus.Completed => 0,
            PipelineJobStatus.Failed => 0,
            PipelineJobStatus.AwaitingUserAction => 2,
            _ when _currentActivity == PipelineActivity.WaitingForLlmResponse => 5,
            _ when _currentActivity == PipelineActivity.RunningVerification => 2,
            _ when _currentActivity == PipelineActivity.RunningRepair => 2,
            _ => 3,
        };
    }

    private int CalculateProgressPercent()
    {
        if (_status == PipelineJobStatus.Completed)
        {
            return 100;
        }

        if (_phaseStates.Count == 0)
        {
            return 0;
        }

        var phaseWeight = 100d / _phaseStates.Count;
        var completedCount = _phaseStates.Count(state => state.Status == PipelinePhaseStatus.Completed);
        var progress = completedCount * phaseWeight;

        if (_currentPhase.HasValue)
        {
            var phaseIndex = PipelinePhaseCatalog.IndexOf(_currentPhase.Value);
            if (phaseIndex >= 0)
            {
                var phaseBase = phaseIndex * phaseWeight;
                progress = Math.Max(progress, phaseBase + (phaseWeight * GetActivityProgress(_currentActivity)));
            }
        }
        else if (_currentActivity == PipelineActivity.HydratingUserContext)
        {
            progress = Math.Max(progress, 4d);
        }

        if (_status == PipelineJobStatus.AwaitingUserAction && _currentPhase.HasValue)
        {
            var awaitingApprovalProgress = (PipelinePhaseCatalog.IndexOf(_currentPhase.Value) + 1) * phaseWeight;
            progress = Math.Max(progress, awaitingApprovalProgress);
        }

        return Math.Clamp((int)Math.Round(progress, MidpointRounding.AwayFromZero), 0, 99);
    }

    private static double GetActivityProgress(PipelineActivity activity)
    {
        return activity switch
        {
            PipelineActivity.Queued => 0d,
            PipelineActivity.HydratingUserContext => 0.1d,
            PipelineActivity.PreparingPrompt => 0.25d,
            PipelineActivity.SendingTaskToLlm => 0.45d,
            PipelineActivity.WaitingForLlmResponse => 0.65d,
            PipelineActivity.ParsingResponse => 0.8d,
            PipelineActivity.RunningVerification => 0.9d,
            PipelineActivity.RunningRepair => 0.93d,
            PipelineActivity.PersistingArtifacts => 0.98d,
            PipelineActivity.AwaitingUserApproval => 1d,
            PipelineActivity.Completed => 1d,
            PipelineActivity.Failed => 1d,
            _ => 0d,
        };
    }

    private static string BuildStatusMessage(PipelinePhase? phase, PipelineActivity activity)
    {
        var phaseName = phase.HasValue ? PipelinePhaseCatalog.Get(phase.Value).DisplayName : "Pipeline intake";

        return activity switch
        {
            PipelineActivity.Queued => $"{phaseName}: queued.",
            PipelineActivity.HydratingUserContext => "Hydrating user profile, preferences, and documents.",
            PipelineActivity.PreparingPrompt => $"{phaseName}: preparing prompt and input payload.",
            PipelineActivity.SendingTaskToLlm => $"{phaseName}: sending task to LLM.",
            PipelineActivity.WaitingForLlmResponse => $"{phaseName}: waiting for LLM response.",
            PipelineActivity.ParsingResponse => $"{phaseName}: parsing model response.",
            PipelineActivity.RunningVerification => $"{phaseName}: running verification.",
            PipelineActivity.RunningRepair => $"{phaseName}: repairing output and re-running verification.",
            PipelineActivity.PersistingArtifacts => $"{phaseName}: persisting output artifacts.",
            PipelineActivity.AwaitingUserApproval => $"{phaseName}: awaiting manual approval.",
            PipelineActivity.Completed => $"{phaseName}: completed.",
            PipelineActivity.Failed => $"{phaseName}: failed.",
            _ => phaseName,
        };
    }

    private PhaseState GetPhaseState(PipelinePhase phase)
    {
        return _phaseStates.Single(state => state.Definition.Phase == phase);
    }

    private PipelinePhaseSnapshot CreatePhaseSnapshot(PhaseState phaseState)
    {
        return new PipelinePhaseSnapshot(
            phaseState.Definition.Phase,
            phaseState.Definition.DisplayName,
            phaseState.Status,
            phaseState.CurrentActivity,
            phaseState.StatusMessage,
            phaseState.AttemptCount,
            phaseState.RepairAttemptCount,
            phaseState.WarningCount,
            phaseState.ErrorCount,
            phaseState.ApprovalRequired,
            phaseState.StartedAtUtc,
            phaseState.CompletedAtUtc,
            phaseState.ApprovedAtUtc,
            phaseState.Artifacts.ToArray());
    }

    private void AddArtifacts(PhaseState phaseState, IEnumerable<PipelineArtifactReference>? artifacts)
    {
        if (artifacts is null)
        {
            return;
        }

        foreach (var artifact in artifacts)
        {
            if (phaseState.Artifacts.All(existing => existing.RelativePath != artifact.RelativePath))
            {
                phaseState.Artifacts.Add(artifact);
            }

            if (_artifacts.All(existing => existing.RelativePath != artifact.RelativePath))
            {
                _artifacts.Add(artifact);
            }
        }
    }

    private void AppendEvent(PipelineEventType eventType, DateTimeOffset timestamp)
    {
        _events.Add(new PipelineEventEnvelope(
            $"{_jobId}:{_events.Count + 1}",
            eventType,
            _jobId,
            _status,
            _currentPhase,
            _currentActivity,
            eventType == PipelineEventType.JobCompleted ? 100 : CalculateProgressPercent(),
            _statusMessage,
            timestamp));
    }

    private sealed class PhaseState
    {
        public PhaseState(PipelinePhaseDefinition definition)
        {
            Definition = definition;
            StatusMessage = $"{definition.DisplayName}: pending.";
        }

        public PipelinePhaseDefinition Definition { get; }

        public PipelinePhaseStatus Status { get; set; } = PipelinePhaseStatus.Pending;

        public PipelineActivity? CurrentActivity { get; set; }

        public string StatusMessage { get; set; }

        public int AttemptCount { get; set; }

        public int RepairAttemptCount { get; set; }

        public int WarningCount { get; set; }

        public int ErrorCount { get; set; }

        public bool ApprovalRequired { get; set; }

        public DateTimeOffset? StartedAtUtc { get; set; }

        public DateTimeOffset? CompletedAtUtc { get; set; }

        public DateTimeOffset? ApprovedAtUtc { get; set; }

        public List<PipelineArtifactReference> Artifacts { get; } = [];
    }
}