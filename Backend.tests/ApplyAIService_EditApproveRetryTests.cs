using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.tests;

public sealed class ApplyAIService_EditApproveRetryTests
{
    [Fact]
    public async Task UpdatePhaseDocumentAsync_RejectsUndefinedOrNullDocumentJson()
    {
        using var harness = new ApplyAiServiceHarness();
        var request = new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = default };

        var act = () => harness.Service.UpdatePhaseDocumentAsync(harness.Principal, Guid.NewGuid().ToString("N"), PipelinePhase.Requirements, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*DocumentJson is required.*");
    }

    [Fact]
    public async Task UpdatePhaseDocumentAsync_RejectsNonEditablePhaseStates()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements)
            .WithPhaseState(PipelinePhase.Requirements, PipelinePhaseStatus.Running));

        var act = () => harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Requirements,
            new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = ApplyAiTestData.JsonElement(new { edited = true }) },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not available for editing*");
    }

    [Fact]
    public async Task EditingTheCurrentManualReviewPhase_ChangesStatusToAwaitingApproval()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.Requirements, PipelineActivity.AwaitingUserApproval)
            .WithPhaseState(
                PipelinePhase.Requirements,
                PipelinePhaseStatus.AwaitingApproval,
                documentId: "doc-1",
                documentJson: ApplyAiTestData.WrapPhaseDocument(new { requirements = new[] { new { requirement_id = "REQ-1" } } }),
                verificationJson: ApplyAiTestData.Json(new { status = "pass" }),
                gateJson: ApplyAiTestData.Json(new { approvedForDownstream = true }),
                approvalRequired: true,
                approvedForDownstream: false,
                hasUnverifiedEdits: false));

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Requirements,
            new ApplyAiPhaseDocumentUpdateRequest
            {
                DocumentJson = ApplyAiTestData.JsonElement(new { requirements = new[] { new { requirement_id = "REQ-1", edited = true } } }),
                EditorComment = "Adjusted requirement wording.",
            },
            TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Requirements,
            TestContext.Current.CancellationToken);

        phaseState.Status.Should().Be(PipelinePhaseStatus.AwaitingApproval);
        phaseState.ApprovalRequired.Should().BeTrue();
    }

    [Theory]
    [InlineData(PipelineJobStatus.Completed)]
    [InlineData(PipelineJobStatus.Failed)]
    public async Task EditingAPersistedPhaseOnCompletedOrFailedJobs_LeavesThePhaseCompleted(PipelineJobStatus status)
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(status, PipelinePhase.Matching, PipelineActivity.Completed)
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = Array.Empty<object>() }));

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Matching,
            new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = ApplyAiTestData.JsonElement(new { matches = new[] { new { requirement_id = "REQ-1" } } }) },
            TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Matching,
            TestContext.Current.CancellationToken);

        phaseState.Status.Should().Be(PipelinePhaseStatus.Completed);
    }

    [Fact]
    public async Task Editing_SetsHasUnverifiedEditsAndClearsApprovedForDownstream()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateCompletedMatchingJob(harness);

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Matching,
            new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = ApplyAiTestData.JsonElement(new { matches = new[] { new { requirement_id = "REQ-1", edited = true } } }) },
            TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Matching,
            TestContext.Current.CancellationToken);

        phaseState.HasUnverifiedEdits.Should().BeTrue();
        phaseState.ApprovedForDownstream.Should().BeFalse();
    }

    [Fact]
    public async Task Editing_RebuildsVerificationJsonWithPendingRevalidationAndIncludesEditorComment()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateCompletedMatchingJob(harness);

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Matching,
            new ApplyAiPhaseDocumentUpdateRequest
            {
                DocumentJson = ApplyAiTestData.JsonElement(new { matches = Array.Empty<object>() }),
                EditorComment = "Removed unsupported match.",
            },
            TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Matching,
            TestContext.Current.CancellationToken);

        phaseState.VerificationJson.Should().Contain("pending_revalidation");
        phaseState.VerificationJson.Should().Contain("Removed unsupported match.");
    }

    [Fact]
    public async Task Editing_RebuildsAndPersistsPhaseArtifacts()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateCompletedMatchingJob(harness);

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Matching,
            new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = ApplyAiTestData.JsonElement(new { matches = Array.Empty<object>() }) },
            TestContext.Current.CancellationToken);

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
    public async Task ApprovePhaseAsync_RejectsPhasesThatAreNotAwaitingManualReview()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Completed, PipelinePhase.Matching, PipelineActivity.Completed)
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = Array.Empty<object>() }));

        var act = () => harness.Service.ApprovePhaseAsync(harness.Principal, job.Id.ToString("N"), PipelinePhase.Matching, cancellationToken: TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not awaiting manual review*");
    }

    [Fact]
    public async Task Approval_ClearsHasUnverifiedEditsAndMarksTheGateDownstreamApproved()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.Matching, PipelineActivity.AwaitingUserApproval)
            .WithPhaseState(
                PipelinePhase.Matching,
                PipelinePhaseStatus.AwaitingApproval,
                documentId: "doc-1",
                documentJson: ApplyAiTestData.WrapPhaseDocument(new { matches = Array.Empty<object>() }),
                verificationJson: ApplyAiTestData.Json(new { status = "pending_revalidation" }),
                gateJson: ApplyAiTestData.Json(new { approvedForDownstream = false }),
                approvalRequired: true,
                approvedForDownstream: false,
                hasUnverifiedEdits: true));

        await harness.Service.ApprovePhaseAsync(harness.Principal, job.Id.ToString("N"), PipelinePhase.Matching, new ApplyAiPhaseApprovalRequest { Comment = "Looks correct." }, TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.Matching,
            TestContext.Current.CancellationToken);

        phaseState.HasUnverifiedEdits.Should().BeFalse();
        phaseState.ApprovedForDownstream.Should().BeTrue();
        phaseState.GateJson.Should().NotBeNull();
        using var gate = JsonDocument.Parse(phaseState.GateJson);
        gate.RootElement.GetProperty("approvedForDownstream").GetBoolean().Should().BeTrue();
        gate.RootElement.GetProperty("hasPendingEdits").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task ApprovingTheFinalPhase_MarksTheWholeJobCompleted()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.ApplicationGeneration, PipelineActivity.AwaitingUserApproval)
            .WithPhaseState(
                PipelinePhase.ApplicationGeneration,
                PipelinePhaseStatus.AwaitingApproval,
                documentId: "doc-1",
                documentJson: ApplyAiTestData.WrapPhaseDocument(new { assembled_application_da = "Hello" }),
                verificationJson: ApplyAiTestData.Json(new { status = "pending_revalidation" }),
                gateJson: ApplyAiTestData.Json(new { approvedForDownstream = false }),
                approvalRequired: true,
                approvedForDownstream: false,
                hasUnverifiedEdits: true));

        var snapshot = await harness.Service.ApprovePhaseAsync(harness.Principal, job.Id.ToString("N"), PipelinePhase.ApplicationGeneration, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Status.Should().Be(PipelineJobStatus.Completed);
        snapshot.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task ApprovingANonFinalPhase_QueuesTheNextPhaseAndResumesExecution()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.AwaitingUserAction, PipelinePhase.CompanyContext, PipelineActivity.AwaitingUserApproval)
            .WithStoredJobPostingArtifact()
            .WithPhaseState(
                PipelinePhase.CompanyContext,
                PipelinePhaseStatus.AwaitingApproval,
                documentId: "doc-1",
                documentJson: ApplyAiTestData.WrapPhaseDocument(new { company_profile = new { industry_da = "Public" } }),
                verificationJson: ApplyAiTestData.Json(new { status = "pass" }),
                gateJson: ApplyAiTestData.Json(new { approvedForDownstream = false }),
                approvalRequired: true,
                approvedForDownstream: false,
                hasUnverifiedEdits: false));

        var snapshot = await harness.Service.ApprovePhaseAsync(harness.Principal, job.Id.ToString("N"), PipelinePhase.CompanyContext, cancellationToken: TestContext.Current.CancellationToken);

        snapshot.Status.Should().Be(PipelineJobStatus.AwaitingUserAction);
        snapshot.CurrentPhase.Should().Be(PipelinePhase.Requirements);
        harness.StageOneRuntime.Verify(runtime => runtime.GenerateRequirementsAsync(
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RetryPhaseAsync_AppliesOverridesAndInvalidatesDownstreamPhasesBeforeRerun()
    {
        using var harness = new ApplyAiServiceHarness();
        var candidateFile = harness.AddUserFile("resume.pdf", consented: true);
        var candidateSummary = new ApplyAiCandidateFileSummary(candidateFile.Id, candidateFile.FileName, DateTime.SpecifyKind(candidateFile.UploadTime, DateTimeKind.Utc));

        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Completed, currentActivity: PipelineActivity.Completed, statusMessage: "Pipeline completed.")
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(candidateSummary)
            .WithCompletedPhaseDocument(PipelinePhase.CompanyContext, new { company_profile = new { industry_da = "Public" } })
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } })
            .WithCompletedPhaseDocument(PipelinePhase.CandidateEvidence, new { evidence_items = new[] { new { evidence_id = "EVID-1", relevant_requirement_ids = new[] { "REQ-1" } } } })
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = new[] { new { requirement_id = "REQ-1", matched_evidence_ids = new[] { "EVID-1" } } }, overall_assessment = new { overall_match_level = "strong" } })
            .WithCompletedPhaseDocument(PipelinePhase.ApplicationGeneration, new { assembled_application_da = "Hello" }));

        var snapshot = await harness.Service.RetryPhaseAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.CompanyContext,
            new ApplyAiPhaseRetryRequest
            {
                CompanyContextOverrides = new ApplyAiCompanyContextOverrides
                {
                    CompanyName = "Updated Kommune",
                    ApplicantAddressHint = "Aarhus",
                },
            },
            TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        snapshot.Status.Should().Be(PipelineJobStatus.Completed);
        reloaded.CompanyNameOverride.Should().Be("Updated Kommune");
        reloaded.ApplicantAddressHint.Should().Be("Aarhus");
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.CompanyContext).AttemptCount.Should().BeGreaterThan(1);
        reloaded.PhaseStates.Single(item => item.Phase == PipelinePhase.ApplicationGeneration).DocumentId.Should().EndWith("attempt-2");
    }

    private static ApplyAiPipelineJob CreateCompletedMatchingJob(ApplyAiServiceHarness harness)
    {
        return harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Completed, currentActivity: PipelineActivity.Completed, statusMessage: "Pipeline completed.")
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = Array.Empty<object>() }));
    }
}