using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.tests;

public sealed class ApplyAIService_SubmissionTests
{
    [Fact]
    public async Task SubmitJobAsync_ThrowsOnNullClaimsPrincipal()
    {
        using var harness = new ApplyAiServiceHarness();

        var act = () => harness.Service.SubmitJobAsync(null!, harness.DefaultRequest, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubmitJobAsync_ThrowsOnNullRequest()
    {
        using var harness = new ApplyAiServiceHarness();

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, null!, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SubmitUploadedJobAsync_RejectsMissingOrEmptyJobPostingFile()
    {
        using var harness = new ApplyAiServiceHarness();
        var request = new ApplyAiJobUploadRequest
        {
            JobPostingFile = ApplyAiTestData.CreateFormFile(content: string.Empty),
        };

        var act = () => harness.Service.SubmitUploadedJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*JobPostingFile is required.*");
    }

    [Fact]
    public async Task SubmitLinkedJobAsync_RejectsNonHttpAndNonHttpsUrls()
    {
        using var harness = new ApplyAiServiceHarness();
        var request = new ApplyAiJobLinkRequest
        {
            Url = "ftp://example.com/job-posting.pdf",
            PreferencesOverride = ApplyAiTestData.JsonElement(new { applicant_display_name = "Test Applicant" }),
        };

        var act = () => harness.Service.SubmitLinkedJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*HTTP or HTTPS*");
    }

    [Fact]
    public async Task Submission_RejectsMissingActiveTermsConsent()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, includeActiveTerms: true, acceptActiveTerms: false);

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, harness.DefaultRequest, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("missing_active_terms_consent*");
    }

    [Fact]
    public async Task Submission_RejectsRunsWithZeroResolvedCandidateFiles()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: false, relevantDocumentCount: 0, includeActiveTerms: true, acceptActiveTerms: true);

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = false,
                IncludeProfileRelevantDocuments = false,
                IncludeAllConsentedFiles = false,
            },
        };

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("missing_candidate_documents*");
    }

    [Fact]
    public async Task Submission_RejectsRunsWithMissingPreferences()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, includeActiveTerms: true, acceptActiveTerms: true);

        var request = harness.DefaultRequest with
        {
            PreferencesOverride = null,
        };

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("missing_preferences*");
    }

    [Fact]
    public async Task SubmitUploadedJobAsync_StoresTheJobPostingArtifactAndQueuesTheNewJob()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, relevantDocumentCount: 1, includeActiveTerms: true, acceptActiveTerms: true);

        var request = CreateUploadRequest();
        var response = await harness.Service.SubmitUploadedJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .Include(item => item.Events)
            .Include(item => item.PhaseStates)
            .SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        harness.ArtifactStorage.Verify(service => service.StoreJobPostingAsync(
            job.Id,
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            request.JobPostingFile.FileName,
            request.JobPostingFile.ContentType,
            It.IsAny<CancellationToken>()), Times.Once);
        harness.ExecutionQueue.Verify(service => service.QueueAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        job.JobPostingSourceType.Should().Be(PipelineInputKind.UploadedFile);
        job.Artifacts.Should().ContainSingle(artifact => artifact.Phase == null && artifact.IsPrimary);
    }

    [Fact]
    public async Task SubmitLinkedJobAsync_PersistsRemoteUrlJobsWithoutCreatingAStoredPostingArtifactUpFront()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, includeActiveTerms: true, acceptActiveTerms: true);

        var request = new ApplyAiJobLinkRequest
        {
            Url = "https://example.com/jobs/frontend-developer",
            CompanyName = "Acme Kommune",
            WorkflowMode = PipelineWorkflowMode.Auto,
            PreferencesOverride = ApplyAiTestData.JsonElement(new { applicant_display_name = "Test Applicant" }),
            RequestedArtifacts = new ApplyAiRequestedArtifacts
            {
                IncludeCoverLetter = true,
                IncludeFitAdvisory = true,
            },
        };

        var response = await harness.Service.SubmitLinkedJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        harness.ArtifactStorage.Verify(service => service.StoreJobPostingAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<Stream>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        job.JobPostingSourceType.Should().Be(PipelineInputKind.RemoteUrl);
        job.JobPostingReference.Should().Be(request.Url);
        job.Artifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task JobCreation_InitializesEveryPhaseAndAppendsAJobAcceptedEvent()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, relevantDocumentCount: 1, includeActiveTerms: true, acceptActiveTerms: true);

        var response = await harness.Service.SubmitJobAsync(harness.Principal, harness.DefaultRequest, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .Include(item => item.Events)
            .SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        job.PhaseStates.Select(item => item.Phase).Should().Equal(PipelinePhaseCatalog.All.Select(definition => definition.Phase));
        job.PhaseStates.Should().OnlyContain(item => item.Status == PipelinePhaseStatus.Pending);
        job.Events.Should().ContainSingle(item => item.EventType == PipelineEventType.JobAccepted);
    }

    private static ApplyAiJobUploadRequest CreateUploadRequest()
    {
        return new ApplyAiJobUploadRequest
        {
            JobPostingFile = ApplyAiTestData.CreateFormFile(),
            WorkflowMode = PipelineWorkflowMode.Auto,
            IncludeCurrentCv = true,
            IncludeProfileRelevantDocuments = true,
            IncludeAllConsentedFiles = false,
            CompanyName = "Acme Kommune",
            ApplicantAddressHint = "Odense",
            PreferencesOverrideJson = ApplyAiTestData.Json(new { applicant_display_name = "Test Applicant" }),
            RequestedArtifactsJson = ApplyAiTestData.Json(new ApplyAiRequestedArtifacts
            {
                IncludeCoverLetter = true,
                IncludeFitAdvisory = true,
            }),
            CorrelationId = "frontend-upload-test",
        };
    }
}