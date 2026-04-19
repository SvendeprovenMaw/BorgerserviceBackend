using System.Text;
using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Backend.tests;

public sealed class ApplyAIService_ArtifactContentTests
{
    [Fact]
    public async Task EnsureStoredJobPostingAsync_NoOpsWhenAStoredPrimaryJobPostingArtifactAlreadyExists()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithJobPosting(PipelineInputKind.RemoteUrl, "https://example.com/jobs/backend")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

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
    public async Task EnsureStoredJobPostingAsync_RejectsNonRemoteJobsThatDoNotHaveAStoredPostingArtifact()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithJobPosting(PipelineInputKind.UploadedFile, "upload://job-posting.pdf", fileName: "job-posting.pdf")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);
        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("No stored job posting artifact was found");
    }

    [Fact]
    public async Task EnsureStoredJobPostingAsync_RejectsInvalidStoredRemoteUrls()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithJobPosting(PipelineInputKind.RemoteUrl, "not-a-valid-url")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs.SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);
        reloaded.Status.Should().Be(PipelineJobStatus.Failed);
        reloaded.StatusMessage.Should().Contain("missing or invalid");
    }

    [Fact]
    public async Task EnsureStoredJobPostingAsync_RendersTheJobPostingToPdfStoresItAndUpdatesFileMetadataOnTheJob()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithJobPosting(PipelineInputKind.RemoteUrl, "https://example.com/jobs/backend")
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued));

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);

        reloaded.JobPostingOriginalFileName.Should().Be("rendered-job-posting.pdf");
        reloaded.JobPostingContentType.Should().Be("application/pdf");
        reloaded.Artifacts.Should().ContainSingle(artifact => artifact.Phase == null && artifact.IsPrimary && artifact.DisplayName == "rendered-job-posting.pdf");
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
    public async Task BuildArtifacts_CreatesDocumentVerificationGateAndMetadataArtifactsForNonApplicationPhases()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithWorkflowMode(PipelineWorkflowMode.Manual)
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.CompanyContext, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact());

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var artifacts = await harness.Db.ApplyAiPipelineArtifacts
            .Where(item => item.JobId == job.Id && item.Phase == PipelinePhase.CompanyContext)
            .OrderBy(item => item.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);

        artifacts.Should().HaveCount(4);
        artifacts.Select(item => item.ArtifactKind).Should().BeEquivalentTo(
            [
                PipelineArtifactKind.JsonDocument,
                PipelineArtifactKind.VerificationReport,
                PipelineArtifactKind.GateReport,
                PipelineArtifactKind.Other,
            ]);
        artifacts.Select(item => item.RelativePath).Should().BeEquivalentTo(
            [
                StoragePathBuilder.BuildPhaseDocumentRelativePath(PipelinePhase.CompanyContext),
                StoragePathBuilder.BuildPhaseVerificationRelativePath(PipelinePhase.CompanyContext),
                StoragePathBuilder.BuildPhaseGateRelativePath(PipelinePhase.CompanyContext),
                StoragePathBuilder.BuildPhaseMetadataRelativePath(PipelinePhase.CompanyContext),
            ]);
    }

    [Fact]
    public async Task BuildArtifacts_IncludesHtmlCssPdfAdvisoryAndRenderSummaryArtifactsWhenApplicationGenerationFlagsRequestThem()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateApplicationGenerationExecutionJob(harness);

        await harness.Service.ExecuteQueuedJobAsync(job.Id, TestContext.Current.CancellationToken);

        var artifacts = await harness.Db.ApplyAiPipelineArtifacts
            .Where(item => item.JobId == job.Id && item.Phase == PipelinePhase.ApplicationGeneration)
            .ToListAsync(TestContext.Current.CancellationToken);

        artifacts.Should().HaveCount(8);
        artifacts.Select(item => item.ArtifactKind).Should().BeEquivalentTo(
            [
                PipelineArtifactKind.JsonDocument,
                PipelineArtifactKind.VerificationReport,
                PipelineArtifactKind.GateReport,
                PipelineArtifactKind.HtmlDocument,
                PipelineArtifactKind.CssStylesheet,
                PipelineArtifactKind.PdfDocument,
                PipelineArtifactKind.Advisory,
                PipelineArtifactKind.Other,
            ]);
    }

    [Fact]
    public async Task BuildComputedArtifactContentAsync_ReturnsGeneratedHtmlCssPdfAdvisoryAndSummaryContentForApplicationGeneration()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = CreateApplicationArtifactContentJob(harness);

        var htmlArtifact = job.Artifacts.Single(item => item.ArtifactKind == PipelineArtifactKind.HtmlDocument);
        var cssArtifact = job.Artifacts.Single(item => item.ArtifactKind == PipelineArtifactKind.CssStylesheet);
        var pdfArtifact = job.Artifacts.Single(item => item.ArtifactKind == PipelineArtifactKind.PdfDocument);
        var advisoryArtifact = job.Artifacts.Single(item => item.ArtifactKind == PipelineArtifactKind.Advisory);
        var summaryArtifact = job.Artifacts.Single(item => item.ArtifactKind == PipelineArtifactKind.Other);

        var html = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), htmlArtifact.Id.ToString("N"), TestContext.Current.CancellationToken);
        var css = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), cssArtifact.Id.ToString("N"), TestContext.Current.CancellationToken);
        var pdf = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), pdfArtifact.Id.ToString("N"), TestContext.Current.CancellationToken);
        var advisory = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), advisoryArtifact.Id.ToString("N"), TestContext.Current.CancellationToken);
        var summary = await harness.Service.GetArtifactContentAsync(harness.Principal, job.Id.ToString("N"), summaryArtifact.Id.ToString("N"), TestContext.Current.CancellationToken);

        html.MediaType.Should().Be("text/html");
        Encoding.UTF8.GetString(html.Content).Should().Contain("Rendered");
        css.MediaType.Should().Be("text/css");
        Encoding.UTF8.GetString(css.Content).Should().Contain("color: black");
        pdf.MediaType.Should().Be("application/pdf");
        Encoding.UTF8.GetString(pdf.Content).Should().Contain("%PDF-1.7");
        advisory.MediaType.Should().Be("application/json");
        Encoding.UTF8.GetString(advisory.Content).Should().Contain("overallMatchLevel");
        summary.MediaType.Should().Be("application/json");
        Encoding.UTF8.GetString(summary.Content).Should().Contain("mainContentCharacterCount");
    }

    [Fact]
    public async Task EnsureRunStoragePrefix_RewritesOldOrMissingRunPrefixesToTheCanonicalLayout()
    {
        using var harness = new ApplyAiServiceHarness();
        var job = harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Completed, PipelinePhase.Matching, PipelineActivity.Completed)
            .WithCompletedPhaseDocument(PipelinePhase.Matching, new { matches = Array.Empty<object>() }));
        job.RunStoragePrefix = "legacy/run-prefix";
        await harness.Db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await harness.Service.UpdatePhaseDocumentAsync(
            harness.Principal,
            job.Id.ToString("N"),
            PipelinePhase.Matching,
            new ApplyAiPhaseDocumentUpdateRequest { DocumentJson = ApplyAiTestData.JsonElement(new { matches = Array.Empty<object>() }) },
            TestContext.Current.CancellationToken);

        var reloaded = await harness.Db.ApplyAiPipelineJobs
            .Include(item => item.Artifacts)
            .SingleAsync(item => item.Id == job.Id, TestContext.Current.CancellationToken);
        var canonicalPrefix = StoragePathBuilder.BuildRunStoragePrefix(reloaded.UserId, reloaded.CreatedAtUtc, reloaded.Id);

        reloaded.RunStoragePrefix.Should().Be(canonicalPrefix);
        reloaded.Artifacts.Where(item => item.Phase == PipelinePhase.Matching).Should().OnlyContain(item => item.StorageKey!.StartsWith(canonicalPrefix, StringComparison.Ordinal));
        harness.ArtifactStorage.Verify(service => service.StoreArtifactAsync(
            It.IsAny<Guid>(),
            canonicalPrefix,
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    private static ApplyAiPipelineJob CreateApplicationGenerationExecutionJob(ApplyAiServiceHarness harness)
    {
        return harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Running, PipelinePhase.ApplicationGeneration, PipelineActivity.Queued)
            .WithStoredJobPostingArtifact()
            .WithRequestedArtifacts(new ApplyAiRequestedArtifacts { IncludeCoverLetter = true, IncludeFitAdvisory = true })
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

    private static ApplyAiPipelineJob CreateApplicationArtifactContentJob(ApplyAiServiceHarness harness)
    {
        return harness.CreatePersistedJob(builder => builder
            .WithStatus(PipelineJobStatus.Completed, PipelinePhase.ApplicationGeneration, PipelineActivity.Completed)
            .WithRequestedArtifacts(new ApplyAiRequestedArtifacts { IncludeCoverLetter = true, IncludeFitAdvisory = true })
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
                },
                assembled_application_da = "Opening paragraph",
            }, documentId: "application-doc")
            .WithArtifact(PipelineArtifactKind.HtmlDocument, "cover_letter.html", StoragePathBuilder.BuildCoverLetterHtmlRelativePath(), PipelinePhase.ApplicationGeneration, mediaType: "text/html")
            .WithArtifact(PipelineArtifactKind.CssStylesheet, "cover_letter.css", StoragePathBuilder.BuildCoverLetterCssRelativePath(), PipelinePhase.ApplicationGeneration, mediaType: "text/css")
            .WithArtifact(PipelineArtifactKind.PdfDocument, "cover_letter.pdf", StoragePathBuilder.BuildCoverLetterPdfRelativePath(), PipelinePhase.ApplicationGeneration, mediaType: "application/pdf")
            .WithArtifact(PipelineArtifactKind.Advisory, "fit_advisory.json", StoragePathBuilder.BuildFitAdvisoryRelativePath(), PipelinePhase.ApplicationGeneration, mediaType: "application/json")
            .WithArtifact(PipelineArtifactKind.Other, "cover_letter_render_summary.json", StoragePathBuilder.BuildCoverLetterSummaryRelativePath(), PipelinePhase.ApplicationGeneration, mediaType: "application/json"));
    }
}