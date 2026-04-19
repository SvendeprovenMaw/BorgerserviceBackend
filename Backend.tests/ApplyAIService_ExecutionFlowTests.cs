using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.tests;

public sealed class ApplyAIService_ExecutionFlowTests
{
    [Fact]
    public async Task ExecuteQueuedJobAsync_NoOpsAlreadyCompletedJobs()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithJobPosting(PipelineInputKind.RemoteUrl, "https://example.com/jobs/backend")
            .WithStatus(PipelineJobStatus.Completed, currentActivity: PipelineActivity.Completed));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Completed);
        harness.JobPostingPdfRenderer.Verify(renderer => renderer.RenderAsync(
            It.IsAny<Uri>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<string, CancellationToken, Task>>()), Times.Never);
        harness.ArtifactStorage.Verify(service => service.StoreJobPostingAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteQueuedJobAsync_NoOpsAlreadyFailedJobs()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithJobPosting(PipelineInputKind.RemoteUrl, "https://example.com/jobs/backend")
            .WithStatus(PipelineJobStatus.Failed, currentActivity: PipelineActivity.Failed, statusMessage: "Already failed."));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Be("Already failed.");
        harness.JobPostingPdfRenderer.Verify(renderer => renderer.RenderAsync(
            It.IsAny<Uri>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<string, CancellationToken, Task>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteQueuedJobAsync_HydratesAMissingRemoteJobPostingPdfBeforePhaseExecution()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var job = harness.CreatePersistedJob(builder => builder
            .WithJobPosting(PipelineInputKind.RemoteUrl, "https://example.com/jobs/backend")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithCandidateFiles(ToCandidateSummary(resume)));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Completed);
        reloaded.JobPostingOriginalFileName.Should().Be("rendered-job-posting.pdf");
        reloaded.JobPostingContentType.Should().Be("application/pdf");
        reloaded.Artifacts.Should().ContainSingle(artifact => artifact.Phase == null && artifact.IsPrimary && !string.IsNullOrWhiteSpace(artifact.StorageKey));
        harness.JobPostingPdfRenderer.Verify(renderer => renderer.RenderAsync(
            It.IsAny<Uri>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<Func<string, CancellationToken, Task>>()), Times.Once);
        harness.ArtifactStorage.Verify(service => service.StoreJobPostingAsync(
            job.Id,
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            "rendered-job-posting.pdf",
            "application/pdf",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteQueuedJobAsync_RethrowsCancellationWhenTheTokenIsCancelled()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        using var cts = new CancellationTokenSource();
        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateCompanyContextAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromCanceled<string>(cts.Token);
            });

        var act = () => harness.Service.ExecuteQueuedJobAsync(job.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteQueuedJobAsync_MarksTheJobFailedWhenUnexpectedExecutionExceptionsOccur()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateCompanyContextAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Company-context generation exploded."));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Events)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("Company-context generation exploded.");
        reloaded.Events.Should().Contain(item => item.EventType == PipelineEventType.JobFailed);
    }

    [Fact]
    public async Task ExecuteUntilBlockedOrCompletedAsync_StartsFromCurrentPhaseWhenPresent()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(ToCandidateSummary(resume)));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.CompanyContext).AttemptCount.Should().Be(0);
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.Requirements).AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteUntilBlockedOrCompletedAsync_StartsFromTheFirstPendingPhaseWhenCurrentPhaseIsNull()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, currentActivity: PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateCompanyContextAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Company context should run first."));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("Company context should run first.");
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.CompanyContext).AttemptCount.Should().Be(1);
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.Requirements).AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteUntilBlockedOrCompletedAsync_CompletesTheJobWhenNoPendingPhasesRemain()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateCompletedPipelineJob(harness, workflowMode: PipelineWorkflowMode.Auto);
        job.Status = PipelineJobStatus.Running;
        job.CurrentPhase = null;
        job.CurrentActivity = PipelineActivity.Queued;
        job.StatusMessage = "Resuming completed pipeline.";
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Events)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Completed);
        reloaded.ProgressPercent.Should().Be(100);
        reloaded.Events.Should().Contain(item => item.EventType == PipelineEventType.JobCompleted);
    }

    [Fact]
    public async Task ExecutePhaseAsync_IncrementsAttemptCountOnNormalRuns()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.CompanyContext,
            TestContext.Current.CancellationToken);

        phaseState.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutePhaseAsync_IncrementsRepairAttemptCountOnRetries()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var job = CreateCompletedPipelineJob(harness, workflowMode: PipelineWorkflowMode.Auto, candidateFiles: [ToCandidateSummary(resume)]);

        await harness.Service.RetryPhaseAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.CompanyContext,
            cancellationToken: TestContext.Current.CancellationToken);

        var companyContext = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.CompanyContext,
            TestContext.Current.CancellationToken);

        companyContext.AttemptCount.Should().Be(2);
        companyContext.RepairAttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecutePhaseAsync_SerializesFailureVerificationAndGateJsonWhenAPhaseThrows()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateRequirementsAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Requirement parsing exploded."));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Requirements,
            TestContext.Current.CancellationToken);

        using var verification = JsonDocument.Parse(phaseState.VerificationJson!);
        using var gate = JsonDocument.Parse(phaseState.GateJson!);

        verification.RootElement.GetProperty("status").GetString().Should().Be("failed");
        verification.RootElement.GetProperty("message").GetString().Should().Be("Requirement parsing exploded.");
        gate.RootElement.GetProperty("approvedForDownstream").GetBoolean().Should().BeFalse();
        gate.RootElement.GetProperty("recommendedAction").GetString().Should().Be("repair_or_regenerate");
    }

    [Fact]
    public async Task ExecutePhaseAsync_MarksTheJobFailedWhenDownstreamApprovalIsDeniedButStillPersistsArtifacts()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        harness.StageOneRuntime
            .Setup(runtime => runtime.VerifyRequirementsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateStageVerificationResult(
                approvedForDownstream: false,
                status: "blocked",
                verificationJson: ApplyAiTestData.Json(new { status = "blocked", approvedForDownstream = false }),
                gateJson: ApplyAiTestData.Json(new { approvedForDownstream = false, recommendedAction = "review" })));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("blocked by verification or downstream gate");
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.Requirements).Status.Should().Be(PipelinePhaseStatus.Failed);
        reloaded.Artifacts.Where(item => item.Phase == PipelinePhase.Requirements).Should().HaveCount(4);
        reloaded.Artifacts.Where(item => item.Phase == PipelinePhase.Requirements).Should().OnlyContain(item => !string.IsNullOrWhiteSpace(item.StorageKey));
        harness.ArtifactStorage.Verify(service => service.StoreArtifactAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task ExecutePhaseAsync_PausesForManualApprovalInManualWorkflowMode()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, currentActivity: PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.AwaitingUserAction);
        reloaded.CurrentPhase.Should().Be(PipelinePhase.CompanyContext);
        reloaded.CurrentActivity.Should().Be(PipelineActivity.AwaitingUserApproval);
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.CompanyContext).Status.Should().Be(PipelinePhaseStatus.AwaitingApproval);
    }

    [Fact]
    public async Task ExecutePhaseAsync_MarksTheJobCompletedAndSetsProgressTo100OnTheFinalSuccessfulAutoPhase()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.ApplicationGeneration, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCompletedPhaseDocument(PipelinePhase.CompanyContext, new { company_profile = new { industry_da = "Public sector" } }, documentId: "company-doc")
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc")
            .WithCompletedPhaseDocument(PipelinePhase.CandidateEvidence, new { evidence_items = new[] { new { evidence_id = "EVID-1", relevant_requirement_ids = new[] { "REQ-1" } } } }, documentId: "candidate-doc")
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new
            {
                matches = new[] { new { requirement_id = "REQ-1", matched_evidence_ids = new[] { "EVID-1" } } },
                overall_assessment = new { overall_match_level = "strong" }
            }, documentId: "matching-doc"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Completed);
        reloaded.ProgressPercent.Should().Be(100);
        reloaded.CurrentPhase.Should().BeNull();
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.ApplicationGeneration).Status.Should().Be(PipelinePhaseStatus.Completed);
    }

    private static ApplyAiCandidateFileSummary ToCandidateSummary(S3File file)
    {
        return new ApplyAiCandidateFileSummary(file.Id, file.FileName, DateTime.SpecifyKind(file.UploadTime, DateTimeKind.Utc));
    }

    private static ApplyAiPipelineJob CreateCompletedPipelineJob(
        ApplyAiServiceHarness harness,
        PipelineWorkflowMode workflowMode,
        ApplyAiCandidateFileSummary[]? candidateFiles = null)
    {
        return harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(workflowMode)
            .WithStatus(PipelineJobStatus.Completed, currentActivity: PipelineActivity.Completed, statusMessage: "Pipeline completed.")
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(candidateFiles ?? [])
            .WithCompletedPhaseDocument(PipelinePhase.CompanyContext, new { company_profile = new { industry_da = "Public sector" } }, documentId: "company-doc")
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc")
            .WithCompletedPhaseDocument(PipelinePhase.CandidateEvidence, new { evidence_items = new[] { new { evidence_id = "EVID-1", relevant_requirement_ids = new[] { "REQ-1" } } } }, documentId: "candidate-doc")
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new
            {
                matches = new[] { new { requirement_id = "REQ-1", matched_evidence_ids = new[] { "EVID-1" } } },
                overall_assessment = new
                {
                    overall_match_level = "strong",
                    major_gap_requirement_ids = Array.Empty<string>(),
                    major_strength_evidence_ids = new[] { "EVID-1" },
                }
            }, documentId: "matching-doc")
            .WithCompletedPhaseDocument(PipelinePhase.ApplicationGeneration, new
            {
                sections = new[]
                {
                    new { section_id = "opening", section_kind = "opening", text_da = "Opening paragraph" },
                    new { section_id = "signature", section_kind = "signature", text_da = "Med venlig hilsen\nTest Applicant" },
                },
                assembled_application_da = "Opening paragraph\n\nMed venlig hilsen\nTest Applicant"
            }, documentId: "application-doc"));
    }
}