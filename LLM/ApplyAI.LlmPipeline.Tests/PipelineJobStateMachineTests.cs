using FluentAssertions;

namespace ApplyAI.LlmPipeline.Tests;

public sealed class PipelineJobStateMachineTests
{
    [Fact]
    public void CreateAcceptedResponse_ExposesStableUrlsAndTransportHints()
    {
        var createdAt = new DateTimeOffset(2026, 4, 18, 11, 0, 0, TimeSpan.Zero);
        var machine = new PipelineJobStateMachine("job-123", CreateRequest(PipelineWorkflowMode.Auto), createdAt);

        var accepted = machine.CreateAcceptedResponse();

        accepted.StatusUrl.Should().Be("/api/ai/pipeline/jobs/job-123");
        accepted.EventsUrl.Should().Be("/api/ai/pipeline/jobs/job-123/events");
        accepted.ApprovePhaseUrlTemplate.Should().Be("/api/ai/pipeline/jobs/job-123/phases/{phase}/approval");
        accepted.RetryPhaseUrlTemplate.Should().Be("/api/ai/pipeline/jobs/job-123/phases/{phase}/retry");
        accepted.SupportsServerSentEvents.Should().BeTrue();
        accepted.RecommendedPollIntervalSeconds.Should().Be(3);
    }

    [Fact]
    public void ManualWorkflow_CompletePhase_WaitsForApproval()
    {
        var createdAt = new DateTimeOffset(2026, 4, 18, 11, 0, 0, TimeSpan.Zero);
        var machine = new PipelineJobStateMachine("job-456", CreateRequest(PipelineWorkflowMode.Manual), createdAt);

        machine.UpdateJobActivity(PipelineActivity.HydratingUserContext, createdAt.AddSeconds(5));
        machine.StartPhase(PipelinePhase.CompanyContext, createdAt.AddSeconds(10));
        machine.UpdatePhaseActivity(PipelinePhase.CompanyContext, PipelineActivity.WaitingForLlmResponse, createdAt.AddSeconds(12));
        machine.CompletePhase(
            PipelinePhase.CompanyContext,
            createdAt.AddSeconds(20),
            [new PipelineArtifactReference(PipelineArtifactKind.JsonDocument, "Results/Run 2/company_context.json", "company_context.json", "application/json", PipelinePhase.CompanyContext, true)]);

        var snapshot = machine.GetSnapshot();

        snapshot.Status.Should().Be(PipelineJobStatus.AwaitingUserAction);
        snapshot.CurrentPhase.Should().Be(PipelinePhase.CompanyContext);
        snapshot.CurrentActivity.Should().Be(PipelineActivity.AwaitingUserApproval);
        snapshot.AvailableActions.Should().ContainSingle(action => action.Action == PipelineActionKind.ApprovePhase);
        snapshot.ProgressPercent.Should().Be(20);
        snapshot.Phases.Should().ContainSingle(phase =>
            phase.Phase == PipelinePhase.CompanyContext &&
            phase.Status == PipelinePhaseStatus.AwaitingApproval &&
            phase.ApprovalRequired);
    }

    [Fact]
    public void ApprovePhase_QueuesNextPhaseForManualWorkflow()
    {
        var createdAt = new DateTimeOffset(2026, 4, 18, 11, 0, 0, TimeSpan.Zero);
        var machine = new PipelineJobStateMachine("job-789", CreateRequest(PipelineWorkflowMode.Manual), createdAt);

        machine.StartPhase(PipelinePhase.CompanyContext, createdAt.AddSeconds(1));
        machine.CompletePhase(PipelinePhase.CompanyContext, createdAt.AddSeconds(10));
        machine.ApprovePhase(PipelinePhase.CompanyContext, createdAt.AddSeconds(15));

        var snapshot = machine.GetSnapshot();
        var companyContext = snapshot.Phases.Single(phase => phase.Phase == PipelinePhase.CompanyContext);

        snapshot.Status.Should().Be(PipelineJobStatus.Running);
        snapshot.CurrentPhase.Should().Be(PipelinePhase.Requirements);
        snapshot.CurrentActivity.Should().Be(PipelineActivity.Queued);
        companyContext.Status.Should().Be(PipelinePhaseStatus.Completed);
        companyContext.ApprovedAtUtc.Should().Be(createdAt.AddSeconds(15));
    }

    [Fact]
    public void AutoWorkflow_CompleteAllPhases_CompletesJob()
    {
        var createdAt = new DateTimeOffset(2026, 4, 18, 11, 0, 0, TimeSpan.Zero);
        var machine = new PipelineJobStateMachine("job-999", CreateRequest(PipelineWorkflowMode.Auto), createdAt);
        var cursor = createdAt.AddSeconds(1);

        foreach (var definition in PipelinePhaseCatalog.All)
        {
            machine.StartPhase(definition.Phase, cursor);
            machine.CompletePhase(
                definition.Phase,
                cursor.AddSeconds(2),
                [new PipelineArtifactReference(PipelineArtifactKind.JsonDocument, $"Results/contracts/{definition.RouteSegment}.json", $"{definition.RouteSegment}.json", "application/json", definition.Phase, true)]);
            cursor = cursor.AddSeconds(5);
        }

        var snapshot = machine.GetSnapshot();

        snapshot.Status.Should().Be(PipelineJobStatus.Completed);
        snapshot.ProgressPercent.Should().Be(100);
        snapshot.Artifacts.Should().HaveCount(PipelinePhaseCatalog.All.Count);
        snapshot.Phases.Should().OnlyContain(phase => phase.Status == PipelinePhaseStatus.Completed);
        machine.GetEvents().Last().EventType.Should().Be(PipelineEventType.JobCompleted);
    }

    [Fact]
    public void PhaseCatalog_MapsExistingPromptAndSchemaFiles()
    {
        var matching = PipelinePhaseCatalog.Get(PipelinePhase.Matching);

        matching.PromptPath.Should().Be("Prompts/matching.prompt");
        matching.OutputSchemaPath.Should().Be("AI Schemas/LLM Parsing/matching_schema.json");
        matching.VerificationSchemaPath.Should().Be("AI Schemas/LLM Verification/requirement_match_verification_schema.json");
        matching.SupportsRepair.Should().BeTrue();
    }

    [Fact]
    public void SubmissionValidator_RejectsNonHttpRemoteUrls()
    {
        var request = new PipelineSubmissionRequest(
            "applicant-1",
            PipelineWorkflowMode.Auto,
            new PipelineSourceReference(PipelineInputKind.RemoteUrl, "ftp://example.com/job.pdf"),
            new PipelineRequestedArtifacts());

        var issues = PipelineSubmissionValidator.Validate(request);

        issues.Should().ContainSingle(issue => issue.Code == "job_listing_url_invalid");
    }

    private static PipelineSubmissionRequest CreateRequest(PipelineWorkflowMode workflowMode)
    {
        return new PipelineSubmissionRequest(
            "applicant-1",
            workflowMode,
            new PipelineSourceReference(
                PipelineInputKind.RemoteUrl,
                "https://example.com/job-listing.pdf"),
            new PipelineRequestedArtifacts());
    }
}