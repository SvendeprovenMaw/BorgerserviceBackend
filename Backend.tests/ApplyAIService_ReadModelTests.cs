using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.tests;

public sealed class ApplyAIService_ReadModelTests
{
    [Fact]
    public async Task GetJobAsync_ReturnsTheOwnedJobSnapshot()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Completed, currentActivity: PipelineActivity.Completed, statusMessage: "Pipeline completed."));

        var snapshot = await harness.Service.GetJobAsync(harness.Principal, job.Id.ToString("N"), TestContext.Current.CancellationToken);

        snapshot.JobId.Should().Be(job.Id.ToString("N"));
        snapshot.Status.Should().Be(PipelineJobStatus.Completed);
        snapshot.CurrentActivity.Should().Be(PipelineActivity.Completed);
    }

    [Fact]
    public async Task GetEventsAsync_ReturnsEventsOrderedByOccurredAtUtcThenEventId()
    {
        using var harness = new ApplyAiServiceHarness();
        var createdAt = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        var job = harness.CreatePersistedJob(builder => builder
            .WithEvent(PipelineEventType.JobProgressUpdated, createdAt.AddMinutes(1), "b", "Second")
            .WithEvent(PipelineEventType.JobAccepted, createdAt, "z", "First timestamp second id")
            .WithEvent(PipelineEventType.JobAccepted, createdAt, "a", "First timestamp first id"));

        var events = await harness.Service.GetEventsAsync(harness.Principal, job.Id.ToString("N"), TestContext.Current.CancellationToken);

        events.Select(item => item.EventId).Should().Equal("a", "z", "b");
    }

    [Fact]
    public async Task GetArtifactsAsync_ReturnsArtifactReferencesOrderedByPhasePrimaryFlagAndDisplayName()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithArtifact(PipelineArtifactKind.Advisory, "fit-advisory.json", "advisory/fit-advisory.json", PipelinePhase.ApplicationGeneration)
            .WithArtifact(PipelineArtifactKind.JsonDocument, "requirements.json", "requirements.json", PipelinePhase.Requirements, isPrimary: true)
            .WithArtifact(PipelineArtifactKind.VerificationReport, "requirements-verification.json", "verification/requirements-verification.json", PipelinePhase.Requirements)
            .WithArtifact(PipelineArtifactKind.JsonDocument, "company-context.json", "company-context.json", PipelinePhase.CompanyContext, isPrimary: true));

        var artifacts = await harness.Service.GetArtifactsAsync(harness.Principal, job.Id.ToString("N"), TestContext.Current.CancellationToken);

        artifacts.Select(item => item.DisplayName).Should().Equal(
            "company-context.json",
            "requirements.json",
            "requirements-verification.json",
            "fit-advisory.json");
    }

    [Fact]
    public async Task GetArtifactContentAsync_RejectsInvalidArtifactIds()
    {
        using var harness = new ApplyAiServiceHarness();

        var act = () => harness.Service.GetArtifactContentAsync(harness.Principal, Guid.NewGuid().ToString("N"), "not-a-guid", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetArtifactContentAsync_DownloadsPersistedArtifactBytesWhenStorageKeyExists()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithArtifact(
                PipelineArtifactKind.JsonDocument,
                "requirements.json",
                "requirements.json",
                PipelinePhase.Requirements,
                isPrimary: true,
                storageKey: "users/test/Runs/job-1/requirements.json"));
        var artifact = job.Artifacts.Single();

        var response = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), artifact.Id.ToString("N"), TestContext.Current.CancellationToken);

        response.FileName.Should().Be("requirements.json");
        response.MediaType.Should().Be("application/json");
        response.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPhaseDocumentAsync_ReturnsParsedDocumentVerificationGateAndPhaseArtifactReferences()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } })
            .WithArtifact(PipelineArtifactKind.JsonDocument, "requirements.json", "requirements.json", PipelinePhase.Requirements, isPrimary: true)
            .WithArtifact(PipelineArtifactKind.VerificationReport, "requirements-verification.json", "verification/requirements-verification.json", PipelinePhase.Requirements));

        var response = await harness.Service.GetPhaseDocumentAsync(harness.Principal, job.Id.ToString("N"), PipelinePhase.Requirements, TestContext.Current.CancellationToken);

        response.Phase.Should().Be(PipelinePhase.Requirements);
        response.DocumentId.Should().NotBeNullOrWhiteSpace();
        response.DocumentJson.ToString().Should().Contain("requirements");
        response.Verification.ToString().Should().Contain("status");
        response.Gate.ToString().Should().Contain("approvedForDownstream");
        response.Artifacts.Should().HaveCount(2);
    }
}