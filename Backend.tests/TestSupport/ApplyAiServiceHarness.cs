using System.Text;
using ApplyAI.Playwright;
using ApplyAI.LlmPipeline;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.tests.TestSupport;

internal sealed class ApplyAiServiceHarness : IDisposable
{
    public ApplyAiServiceHarness()
    {
        Db = ApplyAiDbContextFactory.CreateInMemory();
        CurrentUser = ApplyAiTestData.CreateUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "demo@example.com", "demo-user");
        Principal = ApplyAiTestData.CreatePrincipal(CurrentUser.Id);
        Environment = new TestHostEnvironment();
        Configuration = TestConfiguration.Empty();

        UserService = new Mock<IUserService>();
        ArtifactStorage = new Mock<IApplyAiArtifactStorageService>();
        CandidateEvidenceService = new Mock<ICandidateEvidenceService>();
        StageOneRuntime = new Mock<IApplyAiStageOneRuntime>();
        MatchingService = new Mock<IMatchingService>();
        ApplicationGenerationService = new Mock<IApplicationGenerationService>();
        VerificationOrchestrator = new Mock<IVerificationOrchestrator>();
        DownstreamGateEvaluator = new Mock<IDownstreamGateEvaluator>();
        ExecutionQueue = new Mock<IApplyAiExecutionQueue>();
        JobPostingPdfRenderer = new Mock<IJobPostingPdfRenderer>();
        CoverLetterPdfRenderer = new Mock<ICoverLetterPdfRenderer>();
        CoverLetterTemplateRenderer = new Mock<ICoverLetterTemplateRenderer>();
        StorageService = new Mock<IS3StorageService>();

        UserService
            .Setup(service => service.GetUser(It.IsAny<System.Security.Claims.ClaimsPrincipal>()))
            .ReturnsAsync(CurrentUser);

        ArtifactStorage
            .Setup(service => service.StoreArtifactAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid artifactId, string runPrefix, string relativePath, byte[] _, string fileName, string contentType, CancellationToken _) =>
                new ApplyAiStoredArtifact(
                    artifactId,
                    StoragePathBuilder.BuildRunArtifactStorageKey(runPrefix, relativePath),
                    relativePath,
                    fileName,
                    contentType,
                    Convert.ToHexString(Encoding.UTF8.GetBytes(fileName))));

        ArtifactStorage
            .Setup(service => service.StoreJobPostingAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid artifactId, string runPrefix, Stream _, string fileName, string contentType, CancellationToken _) =>
                new ApplyAiStoredArtifact(
                    Guid.NewGuid(),
                    StoragePathBuilder.BuildRunArtifactStorageKey(runPrefix, $"inputs/job_listing/{fileName}"),
                    $"inputs/job_listing/{fileName}",
                    fileName,
                    contentType,
                    "job-posting-checksum"));

        ArtifactStorage
            .Setup(service => service.DownloadArtifactAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string fileName, string mediaType, CancellationToken _) =>
                new ApplyAiArtifactContentResponse(Encoding.UTF8.GetBytes("artifact-content"), mediaType, fileName));

        StageOneRuntime
            .Setup(runtime => runtime.GenerateCompanyContextAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.Json(new
            {
                _meta = new { company_name = "Acme Kommune" },
                company_profile = new { industry_da = "Public sector" },
            }));

        StageOneRuntime
            .Setup(runtime => runtime.GenerateRequirementsAsync(
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.Json(new
            {
                requirements = new[]
                {
                    new { requirement_id = "REQ-1", requirement_text_da = "Have solid API experience.", importance = "must_have" },
                },
            }));

        StageOneRuntime
            .Setup(runtime => runtime.VerifyRequirementsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateStageVerificationResult());

        CandidateEvidenceService
            .Setup(service => service.GenerateCandidateEvidenceAsync(It.IsAny<CandidateEvidenceGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                evidence_items = new[]
                {
                    new
                    {
                        evidence_id = "EVID-1",
                        fact_da = "Built and maintained APIs",
                        relevant_requirement_ids = new[] { "REQ-1" },
                        citations = new[]
                        {
                            new { filename = "resume.pdf", excerpt = "API project excerpt" },
                        },
                    },
                },
            }));

        MatchingService
            .Setup(service => service.GenerateMatchingAsync(It.IsAny<MatchingGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                matches = new[]
                {
                    new
                    {
                        requirement_id = "REQ-1",
                        verdict = "matched",
                        matched_evidence_ids = new[] { "EVID-1" },
                        rationale_da = "Strong API experience.",
                        confidence = "high",
                        needs_human_review = false,
                    },
                },
                overall_assessment = new
                {
                    overall_match_level = "strong",
                    summary_da = "Strong overall match.",
                    major_gap_requirement_ids = Array.Empty<string>(),
                    major_strength_evidence_ids = new[] { "EVID-1" },
                },
            }));

        ApplicationGenerationService
            .Setup(service => service.GenerateApplicationGenerationAsync(It.IsAny<ApplicationGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateStructuredResult(new
            {
                application_strategy = new
                {
                    subject_line_da = "Ansøgning",
                    core_message_da = "Core message",
                },
                sections = new[]
                {
                    new { section_id = "opening", section_kind = "opening", text_da = "Opening paragraph" },
                    new { section_id = "signature", section_kind = "signature", text_da = "Med venlig hilsen\nTest Applicant" },
                },
                claim_register = new[]
                {
                    new { claim_id = "CLAIM-1", claim_text_da = "API experience", support_strength = "strong" },
                },
                assembled_application_da = "Opening paragraph\n\nMed venlig hilsen\nTest Applicant",
            }));

        VerificationOrchestrator
            .Setup(service => service.VerifyStageAsync(It.IsAny<StageVerificationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StageVerificationRequest request, CancellationToken _) =>
                ApplyAiTestData.CreateVerificationResult(request.Stage, request.DocumentId));

        DownstreamGateEvaluator
            .Setup(service => service.Evaluate(It.IsAny<StageVerificationRequest>(), It.IsAny<StageVerificationResult>()))
            .Returns((StageVerificationRequest request, StageVerificationResult _) => ApplyAiTestData.CreateGateResult(request.Stage.ToString()));

        ExecutionQueue
            .Setup(service => service.QueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        JobPostingPdfRenderer
            .Setup(renderer => renderer.RenderAsync(It.IsAny<Uri>(), It.IsAny<CancellationToken>(), It.IsAny<Func<string, CancellationToken, Task>>()))
            .ReturnsAsync(ApplyAiTestData.CreateRenderedPdfDocument());

        CoverLetterTemplateRenderer
            .Setup(renderer => renderer.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreateTemplateRenderResult(maxMainContentCharacters: CoverLetterContentMetrics.DefaultMaxMainContentCharacters));

        CoverLetterPdfRenderer
            .Setup(renderer => renderer.RenderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApplyAiTestData.CreatePdfRenderResult());

        StorageService
            .Setup(service => service.DownloadFileContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string storageKey, CancellationToken _) => Encoding.UTF8.GetBytes($"download:{storageKey}"));

        JobStore = new ApplyAiJobStore(Db);
        Service = new ApplyAIService(
            ApplicationGenerationService.Object,
            Db,
            UserService.Object,
            JobStore,
            ArtifactStorage.Object,
            CandidateEvidenceService.Object,
            Configuration,
            CoverLetterPdfRenderer.Object,
            CoverLetterTemplateRenderer.Object,
            DownstreamGateEvaluator.Object,
            Environment,
            ExecutionQueue.Object,
            StageOneRuntime.Object,
            JobPostingPdfRenderer.Object,
            MatchingService.Object,
            StorageService.Object,
            VerificationOrchestrator.Object,
            NullLogger<ApplyAIService>.Instance);

        Db.Users.Add(CurrentUser);
        Db.SaveChanges();
    }

    public ApplyAIDbContext Db { get; }

    public ApplyAIService Service { get; }

    public ApplyAiJobStore JobStore { get; }

    public Mock<IApplicationGenerationService> ApplicationGenerationService { get; }

    public Mock<IApplyAiArtifactStorageService> ArtifactStorage { get; }

    public Mock<ICandidateEvidenceService> CandidateEvidenceService { get; }

    public IConfiguration Configuration { get; }

    public User CurrentUser { get; }

    public Mock<ICoverLetterPdfRenderer> CoverLetterPdfRenderer { get; }

    public Mock<ICoverLetterTemplateRenderer> CoverLetterTemplateRenderer { get; }

    public ApplyAiJobRequest DefaultRequest => new()
    {
        WorkflowMode = PipelineWorkflowMode.Auto,
        JobPostingSource = new ApplyAiJobPostingSourceRequest
        {
            SourceType = PipelineInputKind.UploadedFile,
            Reference = "upload://job-posting.pdf",
            FileName = "job-posting.pdf",
            ContentType = "application/pdf",
        },
        CandidateDocuments = new ApplyAiCandidateDocumentSelection
        {
            IncludeCurrentCv = true,
            IncludeProfileRelevantDocuments = true,
            IncludeAllConsentedFiles = false,
        },
        CompanyContextOverrides = new ApplyAiCompanyContextOverrides
        {
            CompanyName = "Acme Kommune",
            ApplicantAddressHint = "Odense",
        },
        PreferencesOverride = ApplyAiTestData.JsonElement(new
        {
            applicant_display_name = "Test Applicant",
            fit_strategy = new { include_fit_advisory = true },
        }),
        RequestedArtifacts = new ApplyAiRequestedArtifacts
        {
            IncludeCoverLetter = true,
            IncludeFitAdvisory = true,
        },
        CorrelationId = "frontend-test-run",
    };

    public Mock<IDownstreamGateEvaluator> DownstreamGateEvaluator { get; }

    public IHostEnvironment Environment { get; }

    public Mock<IApplyAiExecutionQueue> ExecutionQueue { get; }

    public Mock<IJobPostingPdfRenderer> JobPostingPdfRenderer { get; }

    public Mock<IMatchingService> MatchingService { get; }

    public System.Security.Claims.ClaimsPrincipal Principal { get; }

    public Mock<IS3StorageService> StorageService { get; }

    public Mock<IApplyAiStageOneRuntime> StageOneRuntime { get; }

    public Mock<IUserService> UserService { get; }

    public Mock<IVerificationOrchestrator> VerificationOrchestrator { get; }

    public Profile SeedProfile(
        bool includeCurrentCv = true,
        int relevantDocumentCount = 0,
        bool consentCurrentCv = true,
        bool consentRelevantDocuments = true,
        bool includeActiveTerms = true,
        bool acceptActiveTerms = true)
    {
        var profile = new Profile(
            CurrentUser,
            applicantId: "applicant-1",
            fullName: "Test Applicant",
            preferencesJson: ProfileDefaults.SerializePreferences(ProfileDefaults.CreateDefaultPreferences("applicant-1", "Test Applicant")),
            profileEnhancementJson: ProfileDefaults.SerializeProfileEnhancement(ProfileDefaults.CreateDefaultEnhancement()));

        Db.Profiles.Add(profile);

        if (includeCurrentCv)
        {
            var currentCv = ApplyAiTestData.CreateFile(CurrentUser, "resume.pdf");
            Db.S3Files.Add(currentCv);
            profile.SetCurrentCv(currentCv);
            if (consentCurrentCv)
            {
                Db.Consents.Add(ApplyAiTestData.CreateConsent(CurrentUser, currentCv));
            }
        }

        for (var index = 0; index < relevantDocumentCount; index++)
        {
            var document = ApplyAiTestData.CreateFile(CurrentUser, $"relevant-{index + 1}.pdf");
            Db.S3Files.Add(document);
            profile.AddRelevantDocument(document);
            if (consentRelevantDocuments)
            {
                Db.Consents.Add(ApplyAiTestData.CreateConsent(CurrentUser, document));
            }
        }

        if (includeActiveTerms)
        {
            var activeTerm = ApplyAiTestData.CreateActiveTerm(CurrentUser, active: true);
            Db.Term.Add(activeTerm);
            if (acceptActiveTerms)
            {
                Db.Consents.Add(ApplyAiTestData.CreateConsent(CurrentUser, activeTerm));
            }
        }

        Db.SaveChanges();
        return profile;
    }

    public S3File AddUserFile(string fileName, bool consented = true, DateTime? uploadTimeUtc = null)
    {
        var file = ApplyAiTestData.CreateFile(CurrentUser, fileName, uploadTimeUtc: uploadTimeUtc);
        Db.S3Files.Add(file);
        if (consented)
        {
            Db.Consents.Add(ApplyAiTestData.CreateConsent(CurrentUser, file));
        }

        Db.SaveChanges();
        return file;
    }

    public ApplyAiPipelineJob PersistJob(ApplyAiPipelineJob job)
    {
        Db.ApplyAiPipelineJobs.Add(job);
        Db.SaveChanges();
        return job;
    }

    public ApplyAiPipelineJob CreatePersistedJob(Action<ApplyAiPipelineJobBuilder>? configure = null)
    {
        var builder = new ApplyAiPipelineJobBuilder(CurrentUser)
            .WithId(Guid.NewGuid())
            .WithCreatedAt(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        configure?.Invoke(builder);
        var job = builder.Build();
        job.RunStoragePrefix = StoragePathBuilder.BuildRunStoragePrefix(job.UserId, job.CreatedAtUtc, job.Id);
        return PersistJob(job);
    }

    public ApplyAiPipelineJobBuilder CreateJobBuilder()
    {
        return new ApplyAiPipelineJobBuilder(CurrentUser)
            .WithCreatedAt(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
    }

    public void Dispose()
    {
        Db.Dispose();
    }
}