using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.tests;

public sealed class ApplyAIService_PhaseInputAndContextTests
{
    [Fact]
    public async Task ResolveUserContextAsync_IncludesTheCurrentCvWhenRequested()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, relevantDocumentCount: 0, includeActiveTerms: true, acceptActiveTerms: true);

        var response = await harness.Service.SubmitJobAsync(harness.Principal, harness.DefaultRequest, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        DeserializeCandidateFiles(job).Select(file => file.FileName).Should().Contain("resume.pdf");
    }

    [Fact]
    public async Task ResolveUserContextAsync_IncludesProfileRelevantDocumentsWhenRequested()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: false, relevantDocumentCount: 2, includeActiveTerms: true, acceptActiveTerms: true);

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = false,
                IncludeProfileRelevantDocuments = true,
                IncludeAllConsentedFiles = false,
            },
        };

        var response = await harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        DeserializeCandidateFiles(job).Should().HaveCount(2);
    }

    [Fact]
    public async Task ResolveUserContextAsync_IncludesExplicitAdditionalFileIds()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: false, relevantDocumentCount: 0, includeActiveTerms: true, acceptActiveTerms: true);
        var extraFile = harness.AddUserFile("portfolio.pdf", consented: true);

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = false,
                IncludeProfileRelevantDocuments = false,
                AdditionalFileIds = [extraFile.Id],
            },
        };

        var response = await harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        DeserializeCandidateFiles(job).Select(file => file.FileId).Should().Contain(extraFile.Id);
    }

    [Fact]
    public async Task ResolveUserContextAsync_RejectsMissingAdditionalFileIds()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: true, relevantDocumentCount: 0, includeActiveTerms: true, acceptActiveTerms: true);

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = false,
                IncludeProfileRelevantDocuments = false,
                AdditionalFileIds = [Guid.NewGuid()],
            },
        };

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("missing_candidate_documents*");
    }

    [Fact]
    public async Task ResolveUserContextAsync_ExpandsToAllConsentedFilesAndDeduplicatesOverlaps()
    {
        using var harness = new ApplyAiServiceHarness();
        var profile = harness.SeedProfile(includeCurrentCv: true, relevantDocumentCount: 1, includeActiveTerms: true, acceptActiveTerms: true);
        var extraFile = harness.AddUserFile("extra.pdf", consented: true);
        var currentCvId = profile.CurrentCvId!.Value;

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = true,
                IncludeProfileRelevantDocuments = true,
                AdditionalFileIds = [currentCvId, extraFile.Id],
                IncludeAllConsentedFiles = true,
            },
        };

        var response = await harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);
        var job = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == Guid.ParseExact(response.JobId, "N"), TestContext.Current.CancellationToken);

        DeserializeCandidateFiles(job).Select(file => file.FileId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ResolveUserContextAsync_RejectsSelectedFilesWithoutActiveConsent()
    {
        using var harness = new ApplyAiServiceHarness();
        harness.SeedProfile(includeCurrentCv: false, relevantDocumentCount: 0, includeActiveTerms: true, acceptActiveTerms: true);
        var uncompensentedFile = harness.AddUserFile("unconsented.pdf", consented: false);

        var request = harness.DefaultRequest with
        {
            CandidateDocuments = new ApplyAiCandidateDocumentSelection
            {
                IncludeCurrentCv = false,
                IncludeProfileRelevantDocuments = false,
                AdditionalFileIds = [uncompensentedFile.Id],
            },
        };

        var act = () => harness.Service.SubmitJobAsync(harness.Principal, request, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("missing_file_consent*");
    }

    [Fact]
    public async Task MaterializeCandidateFilesAsync_RejectsRunsWithZeroSelectedFileIds()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CandidateEvidence, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles()
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);
        var reloaded = await harness.Db.ApplyAiPipelineJobs.Include(item => item.PhaseStates).SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("no selected candidate files");
    }

    [Fact]
    public async Task MaterializeCandidateFilesAsync_RejectsDuplicateCandidateFilenamesBeforeLlmExecution()
    {
        using var harness = new ApplyAiServiceHarness();
        var fileA = harness.AddUserFile("resume.pdf", consented: true);
        var fileB = harness.AddUserFile("resume.pdf", consented: true);
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CandidateEvidence, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithCandidateFiles(
                new ApplyAiCandidateFileSummary(fileA.Id, fileA.FileName, DateTime.SpecifyKind(fileA.UploadTime, DateTimeKind.Utc)),
                new ApplyAiCandidateFileSummary(fileB.Id, fileB.FileName, DateTime.SpecifyKind(fileB.UploadTime, DateTimeKind.Utc)))
            .WithCompletedPhaseDocument(PipelinePhase.Requirements, new { requirements = new[] { new { requirement_id = "REQ-1" } } }));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);
        var reloaded = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("duplicate file names");
    }

    private static ApplyAiCandidateFileSummary[] DeserializeCandidateFiles(ApplyAiPipelineJob job)
    {
        return JsonSerializer.Deserialize<ApplyAiCandidateFileSummary[]>(job.CandidateFileSnapshotJson)!;
    }
}