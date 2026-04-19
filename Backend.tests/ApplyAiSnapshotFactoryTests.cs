using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;

namespace Backend.tests;

public sealed class ApplyAiSnapshotFactoryTests
{
    [Fact]
    public void CreateAcceptedResponse_BuildsStatusEventAndActionUrls()
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
            .Build();

        var response = ApplyAiSnapshotFactory.CreateAcceptedResponse(job);

        response.JobId.Should().Be("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        response.StatusUrl.Should().Be("/api/ai/pipeline/jobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        response.EventsUrl.Should().Be("/api/ai/pipeline/jobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/events");
        response.ApprovePhaseUrlTemplate.Should().Be("/api/ai/pipeline/jobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/phases/{phase}/approve");
        response.RetryPhaseUrlTemplate.Should().Be("/api/ai/pipeline/jobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/phases/{phase}/retry");
        response.RecommendedPollIntervalSeconds.Should().Be(2);
        response.SupportsServerSentEvents.Should().BeTrue();
    }

    [Fact]
    public void CreateSnapshot_SortsPhaseSnapshotsByCatalogOrder()
    {
        var job = new ApplyAiPipelineJobBuilder().Build();
        var reversed = job.PhaseStates.Reverse().ToArray();
        job.PhaseStates.Clear();
        foreach (var phaseState in reversed)
        {
            job.PhaseStates.Add(phaseState);
        }

        var snapshot = ApplyAiSnapshotFactory.CreateSnapshot(job);

        snapshot.Phases.Select(phase => phase.Phase).Should().Equal(PipelinePhaseCatalog.All.Select(definition => definition.Phase));
    }

    [Fact]
    public void CreateEvents_OrdersByOccurredAtThenEventId()
    {
        var createdAt = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        var job = new ApplyAiPipelineJobBuilder()
            .WithStatus(PipelineJobStatus.Running)
            .WithEvent(PipelineEventType.JobProgressUpdated, createdAt.AddMinutes(1), "b-event", "Second")
            .WithEvent(PipelineEventType.JobAccepted, createdAt, "z-event", "First timestamp second id")
            .WithEvent(PipelineEventType.JobAccepted, createdAt, "a-event", "First timestamp first id")
            .Build();

        var events = ApplyAiSnapshotFactory.CreateEvents(job);

        events.Select(item => item.EventId).Should().Equal("a-event", "z-event", "b-event");
    }

    [Fact]
    public void CreatePhaseDocumentResponse_MarksCurrentManualReviewPhaseEditable()
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.Requirements, PipelineActivity.AwaitingUserApproval)
            .WithPhaseState(
                PipelinePhase.Requirements,
                PipelinePhaseStatus.AwaitingApproval,
                documentId: "job:requirements:attempt-1",
                documentJson: ApplyAiTestData.WrapPhaseDocument(new { ready = true }),
                approvedForDownstream: false,
                approvalRequired: true)
            .Build();

        var response = ApplyAiSnapshotFactory.CreatePhaseDocumentResponse(job, job.PhaseStates.Single(item => item.Phase == PipelinePhase.Requirements));

        response.Editable.Should().BeTrue();
    }

    [Theory]
    [InlineData(PipelineJobStatus.Completed)]
    [InlineData(PipelineJobStatus.Failed)]
    public void CreatePhaseDocumentResponse_MarksPersistedCompletedOrFailedPhasesEditable(PipelineJobStatus status)
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithStatus(status)
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = new[] { "REQ-1" } })
            .Build();

        var response = ApplyAiSnapshotFactory.CreatePhaseDocumentResponse(job, job.PhaseStates.Single(item => item.Phase == PipelinePhase.Matching));

        response.Editable.Should().BeTrue();
    }

    [Fact]
    public void CreateSnapshot_ReturnsApproveAndRetryActionsForAwaitingUserActionJobs()
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.CandidateEvidence, PipelineActivity.AwaitingUserApproval)
            .Build();

        var snapshot = ApplyAiSnapshotFactory.CreateSnapshot(job);

        snapshot.AvailableActions.Select(action => action.Action).Should().Equal(PipelineActionKind.ApprovePhase, PipelineActionKind.RetryPhase);
        snapshot.AvailableActions.Should().OnlyContain(action => action.Phase == PipelinePhase.CandidateEvidence);
    }

    [Fact]
    public void CreateSnapshot_ReturnsRetryOnlyForFailedJobsWithCurrentPhase()
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithStatus(PipelineJobStatus.Failed, PipelinePhase.ApplicationGeneration, PipelineActivity.Failed)
            .Build();

        var snapshot = ApplyAiSnapshotFactory.CreateSnapshot(job);

        snapshot.AvailableActions.Should().ContainSingle();
        snapshot.AvailableActions[0].Action.Should().Be(PipelineActionKind.RetryPhase);
        snapshot.AvailableActions[0].Phase.Should().Be(PipelinePhase.ApplicationGeneration);
    }

    [Fact]
    public void CreateArtifacts_SortsArtifactsAndUsesArtifactContentRoute()
    {
        var job = new ApplyAiPipelineJobBuilder()
            .WithId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
            .WithArtifact(PipelineArtifactKind.Advisory, "fit-advisory.json", "advisory/fit-advisory.json", PipelinePhase.ApplicationGeneration)
            .WithArtifact(PipelineArtifactKind.JsonDocument, "requirements.json", "requirements.json", PipelinePhase.Requirements, isPrimary: true)
            .WithArtifact(PipelineArtifactKind.VerificationReport, "company-context-verification.json", "verification/company_context.json", PipelinePhase.CompanyContext)
            .Build();

        var artifacts = ApplyAiSnapshotFactory.CreateArtifacts(job);

        artifacts.Select(item => item.DisplayName).Should().Equal("company-context-verification.json", "requirements.json", "fit-advisory.json");
        artifacts[1].RelativePath.Should().Contain("/api/ai/pipeline/jobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/artifacts/");
    }
}