using System.Text;
using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Services.ApplyAIService;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.tests;

public sealed class ApplyAIService_PhaseExecutionTests
{
    [Fact]
    public async Task CompanyContextExecution_ForwardsJobPostingBytesOverridesProfileTextAndAddressHint()
    {
        using var harness = new ApplyAiServiceHarness();
        var profile = harness.SeedProfile(includeCurrentCv: false, relevantDocumentCount: 0, includeActiveTerms: false);
        profile.UpdatePersonalDetails(profile.ApplicantId, profile.FullName, "12345678", "Aarhus", "Kort bio", profile.ProfileEnhancementJson);
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        byte[]? capturedContent = null;
        string? capturedFileName = null;
        string? capturedMediaType = null;
        string? capturedCompanyName = null;
        string? capturedProfileText = null;
        string? capturedAddressHint = null;

        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateCompanyContextAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback((byte[] content, string fileName, string mediaType, string? companyName, string? applicantProfileText, string? applicantAddressHint, CancellationToken _) =>
            {
                capturedContent = content;
                capturedFileName = fileName;
                capturedMediaType = mediaType;
                capturedCompanyName = companyName;
                capturedProfileText = applicantProfileText;
                capturedAddressHint = applicantAddressHint;
            })
            .ReturnsAsync(ApplyAiTestData.Json(new { company_profile = new { industry_da = "Public sector" } }));

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());
        job.CompanyNameOverride = "Updated Kommune";
        job.ApplicantAddressHint = "Aalborg";
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(capturedContent!).Should().Be("artifact-content");
        capturedFileName.Should().Be("job-posting.pdf");
        capturedMediaType.Should().Be("application/pdf");
        capturedCompanyName.Should().Be("Updated Kommune");
        capturedProfileText.Should().Contain("Navn: Test Applicant");
        capturedProfileText.Should().Contain("Lokation: Aarhus");
        capturedAddressHint.Should().Be("Aalborg");
    }

    [Fact]
    public async Task CompanyContextExecution_ReturnsNotApplicableVerificationAndAnApprovedGate()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(ToCandidateSummary(resume)));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var phaseState = await harness.Db.ApplyAiPipelinePhaseStates.SingleAsync(
            item => item.JobId == job.Id && item.Phase == PipelinePhase.CompanyContext,
            TestContext.Current.CancellationToken);

        phaseState.Status.Should().Be(PipelinePhaseStatus.Completed);
        phaseState.ApprovedForDownstream.Should().BeTrue();
        phaseState.VerificationJson.Should().Contain("not_applicable");
        phaseState.GateJson.Should().Contain("\"approvedForDownstream\": true");
    }

    [Fact]
    public async Task RequirementsExecution_ForwardsTheStoredJobPostingFileMetadataToStageOneRuntime()
    {
        using var harness = new ApplyAiServiceHarness();
        byte[]? capturedContent = null;
        string? capturedFileName = null;
        string? capturedMediaType = null;

        harness.StageOneRuntime
            .Setup(runtime => runtime.GenerateRequirementsAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((byte[] content, string fileName, string mediaType, CancellationToken _) =>
            {
                capturedContent = content;
                capturedFileName = fileName;
                capturedMediaType = mediaType;
            })
            .ReturnsAsync(ApplyAiTestData.Json(new { requirements = new[] { new { requirement_id = "REQ-1" } } }));

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact(displayName: "listing.pdf", mediaType: "application/pdf"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        Encoding.UTF8.GetString(capturedContent!).Should().Be("artifact-content");
        capturedFileName.Should().Be("listing.pdf");
        capturedMediaType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task RequirementsExecution_PassesTheGeneratedDocumentIdAndSourceFilenameIntoVerification()
    {
        using var harness = new ApplyAiServiceHarness();
        string? capturedDocumentId = null;
        string? capturedDocumentJson = null;
        string? capturedFileName = null;

        harness.StageOneRuntime
            .Setup(runtime => runtime.VerifyRequirementsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback((string documentId, string documentJson, string jobPostingFileName, CancellationToken _) =>
            {
                capturedDocumentId = documentId;
                capturedDocumentJson = documentJson;
                capturedFileName = jobPostingFileName;
            })
            .ReturnsAsync(ApplyAiTestData.CreateStageVerificationResult());

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Requirements, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact(displayName: "listing.pdf", mediaType: "application/pdf"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedDocumentId.Should().Be($"{job.Id:N}:{PipelinePhaseCatalog.ToRouteSegment(PipelinePhase.Requirements)}:attempt-1");
        capturedDocumentJson.Should().Contain("requirements");
        capturedFileName.Should().Be("listing.pdf");
    }

    [Fact]
    public async Task RequirementsExecution_ReturnsABlockedCompletionMessageWhenVerificationOrGateBlocksDownstreamFlow()
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
            .ReturnsAsync(ApplyAiTestData.CreateStageVerificationResult(approvedForDownstream: false, status: "blocked"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("blocked by verification or downstream gate");
    }

    [Fact]
    public async Task CandidateEvidenceExecution_BuildsARequestWithRequirementsDocumentIdRequirementsJsonAndMaterializedCandidateFilePaths()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var portfolio = harness.AddUserFile("portfolio.pdf", consented: true);
        CandidateEvidenceGenerationRequest? capturedRequest = null;

        harness.CandidateEvidenceService
            .Setup(service => service.GenerateCandidateEvidenceAsync(It.IsAny<CandidateEvidenceGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CandidateEvidenceGenerationRequest request, CancellationToken _) => capturedRequest = request)
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                evidence_items = new[]
                {
                    new { evidence_id = "EVID-1", relevant_requirement_ids = new[] { "REQ-1" } },
                },
            }));

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CandidateEvidence, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(ToCandidateSummary(resume), ToCandidateSummary(portfolio))
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequirementsDocumentId.Should().Be("requirements-doc");
        capturedRequest.RequirementsDocumentJson.Should().Contain("REQ-1");
        capturedRequest.CandidateFilePaths.Select(Path.GetFileName).Should().Equal("resume.pdf", "portfolio.pdf");
    }

    [Fact]
    public async Task CandidateEvidenceExecution_BuildsVerificationRequestsWithAllowedCitationFilesAndDisallowedJobPostingFileNames()
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithJobPosting(PipelineInputKind.UploadedFile, "upload://original.pdf", fileName: "original-job-posting.pdf")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CandidateEvidence, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact(displayName: "rendered-job-posting.pdf")
            .WithCandidateFiles(ToCandidateSummary(resume))
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc"));
        StageVerificationRequest? capturedVerificationRequest = null;

        harness.VerificationOrchestrator
            .Setup(service => service.VerifyStageAsync(It.IsAny<StageVerificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((StageVerificationRequest request, CancellationToken _) => capturedVerificationRequest = request)
            .ReturnsAsync((StageVerificationRequest request, CancellationToken _) => ApplyAiTestData.CreateVerificationResult(request.Stage, request.DocumentId));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedVerificationRequest.Should().NotBeNull();
        capturedVerificationRequest!.Stage.Should().Be(VerificationStage.CandidateEvidence);
        capturedVerificationRequest.AllowedCitationFiles.Should().Equal("resume.pdf");
        capturedVerificationRequest.DisallowedCitationFiles.Should().BeEquivalentTo(["original-job-posting.pdf", "rendered-job-posting.pdf"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateEvidenceExecution_DeletesItsTemporaryWorkingDirectoryAfterSuccessAndAfterFailure(bool failGeneration)
    {
        using var harness = new ApplyAiServiceHarness();
        var resume = harness.AddUserFile("resume.pdf", consented: true);
        CandidateEvidenceGenerationRequest? capturedRequest = null;

        harness.CandidateEvidenceService
            .Setup(service => service.GenerateCandidateEvidenceAsync(It.IsAny<CandidateEvidenceGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((CandidateEvidenceGenerationRequest request, CancellationToken _) => capturedRequest = request)
            .Returns(() => failGeneration
                ? throw new InvalidOperationException("Candidate evidence exploded.")
                : Task.FromResult(ApplyAiTestData.CreateStructuredResult(new
                {
                    evidence_items = new[]
                    {
                        new { evidence_id = "EVID-1", relevant_requirement_ids = new[] { "REQ-1" } },
                    },
                })));

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CandidateEvidence, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(ToCandidateSummary(resume))
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        var tempDirectory = Path.GetDirectoryName(capturedRequest!.CandidateFilePaths.Single());
        tempDirectory.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(tempDirectory!).Should().BeFalse();
    }

    [Fact]
    public async Task MatchingExecution_BuildsARequestWithRequirementsAndCandidateEvidenceDocumentIdsPlusJsonPayloads()
    {
        using var harness = new ApplyAiServiceHarness();
        MatchingGenerationRequest? capturedRequest = null;

        harness.MatchingService
            .Setup(service => service.GenerateMatchingAsync(It.IsAny<MatchingGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((MatchingGenerationRequest request, CancellationToken _) => capturedRequest = request)
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                matches = new[]
                {
                    new { requirement_id = "REQ-1", matched_evidence_ids = new[] { "EVID-1" } },
                },
                overall_assessment = new { overall_match_level = "strong" },
            }));

        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.Matching, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }, documentId: "requirements-doc")
            .WithCompletedPhaseDocument(PipelinePhase.CandidateEvidence, new { evidence_items = new[] { new { evidence_id = "EVID-1" } } }, documentId: "candidate-doc"));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequirementsDocumentId.Should().Be("requirements-doc");
        capturedRequest.CandidateEvidenceDocumentId.Should().Be("candidate-doc");
        capturedRequest.RequirementsDocumentJson.Should().Contain("REQ-1");
        capturedRequest.CandidateEvidenceDocumentJson.Should().Contain("EVID-1");
    }

    [Fact]
    public async Task ApplicationGenerationExecution_RejectsJobsWithNoStoredPreferenceSnapshot()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateApplicationGenerationJob(harness);
        job.PreferencesSnapshotJson = null;
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("does not contain a stored preferences snapshot");
    }

    [Fact]
    public async Task ApplicationGenerationExecution_BuildsARequestWithAllUpstreamDocumentIdsAndStoredPreferences()
    {
        using var harness = new ApplyAiServiceHarness();
        ApplicationGenerationRequest? capturedRequest = null;

        harness.ApplicationGenerationService
            .Setup(service => service.GenerateApplicationGenerationAsync(It.IsAny<ApplicationGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((ApplicationGenerationRequest request, CancellationToken _) => capturedRequest = request)
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                sections = new[]
                {
                    new { section_id = "opening", section_kind = "opening", text_da = "Opening paragraph" },
                },
                assembled_application_da = "Opening paragraph",
            }));

        var job = CreateApplicationGenerationJob(harness);
        job.PreferencesSnapshotJson = ApplyAiTestData.Json(new { applicant_display_name = "Updated Applicant", fit_strategy = new { include_fit_advisory = true } });
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ApplicationDocumentId.Should().Be($"{job.Id:N}:{PipelinePhaseCatalog.ToRouteSegment(PipelinePhase.ApplicationGeneration)}:attempt-1");
        capturedRequest.RequirementsDocumentId.Should().Be("requirements-doc");
        capturedRequest.CandidateEvidenceDocumentId.Should().Be("candidate-doc");
        capturedRequest.CompanyContextDocumentId.Should().Be("company-doc");
        capturedRequest.MatchingDocumentId.Should().Be("matching-doc");
        capturedRequest.RequirementsDocumentJson.Should().Contain("REQ-1");
        capturedRequest.CandidateEvidenceDocumentJson.Should().Contain("EVID-1");
        capturedRequest.CompanyContextDocumentJson.Should().Contain("company_profile");
        capturedRequest.MatchingDocumentJson.Should().Contain("overall_match_level");
        capturedRequest.PreferencesJson.Should().Contain("Updated Applicant");
    }

    [Fact]
    public async Task ApplicationGenerationVerification_UsesTheDefaultCoverLetterContentMetricsLimits()
    {
        using var harness = new ApplyAiServiceHarness();
        StageVerificationRequest? capturedVerificationRequest = null;

        harness.VerificationOrchestrator
            .Setup(service => service.VerifyStageAsync(It.IsAny<StageVerificationRequest>(), It.IsAny<CancellationToken>()))
            .Callback((StageVerificationRequest request, CancellationToken _) => capturedVerificationRequest = request)
            .ReturnsAsync((StageVerificationRequest request, CancellationToken _) => ApplyAiTestData.CreateVerificationResult(request.Stage, request.DocumentId));

        var job = CreateApplicationGenerationJob(harness);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        capturedVerificationRequest.Should().NotBeNull();
        capturedVerificationRequest!.Stage.Should().Be(VerificationStage.ApplicationGeneration);
        capturedVerificationRequest.MaxMainContentCharacters.Should().Be(CoverLetterContentMetrics.DefaultMaxMainContentCharacters);
        capturedVerificationRequest.EstimatedCharactersPerLine.Should().Be(CoverLetterContentMetrics.DefaultEstimatedCharactersPerLine);
    }

    private static ApplyAiCandidateFileSummary ToCandidateSummary(S3File file)
    {
        return new ApplyAiCandidateFileSummary(file.Id, file.FileName, DateTime.SpecifyKind(file.UploadTime, DateTimeKind.Utc));
    }

    private static ApplyAiPipelineJob CreateApplicationGenerationJob(ApplyAiServiceHarness harness)
    {
        return harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.ApplicationGeneration, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
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
            }, documentId: "matching-doc"));
    }
}