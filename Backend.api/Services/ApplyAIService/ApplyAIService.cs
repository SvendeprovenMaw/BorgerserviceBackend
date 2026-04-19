using System.Security.Claims;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApplyAI.LlmPipeline;
using ApplyAI.Playwright;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Services;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services.ApplyAIService
{
    public sealed class ApplyAIService : IApplyAIService, IApplyAiJobExecutionService
    {
        private static readonly JsonSerializerOptions RuntimeJsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions ArtifactJsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly IApplicationGenerationService _applicationGenerationService;
        private readonly ApplyAIDbContext _db;
        private readonly IUserService _userService;
        private readonly ApplyAiJobStore _jobStore;
        private readonly IApplyAiArtifactStorageService _artifactStorage;
        private readonly ICandidateEvidenceService _candidateEvidenceService;
        private readonly IConfiguration _configuration;
        private readonly ICoverLetterPdfRenderer _coverLetterPdfRenderer;
        private readonly ICoverLetterTemplateRenderer _coverLetterTemplateRenderer;
        private readonly IDownstreamGateEvaluator _downstreamGateEvaluator;
        private readonly IHostEnvironment _environment;
        private readonly IApplyAiExecutionQueue _executionQueue;
        private readonly IApplyAiStageOneRuntime _stageOneRuntime;
        private readonly IJobPostingPdfRenderer _jobPostingPdfRenderer;
        private readonly ILogger<ApplyAIService> _logger;
        private readonly IMatchingService _matchingService;
        private readonly IS3StorageService _storageService;
        private readonly IVerificationOrchestrator _verificationOrchestrator;

        public ApplyAIService(
            IApplicationGenerationService applicationGenerationService,
            ApplyAIDbContext db,
            IUserService userService,
            ApplyAiJobStore jobStore,
            IApplyAiArtifactStorageService artifactStorage,
            ICandidateEvidenceService candidateEvidenceService,
            IConfiguration configuration,
            ICoverLetterPdfRenderer coverLetterPdfRenderer,
            ICoverLetterTemplateRenderer coverLetterTemplateRenderer,
            IDownstreamGateEvaluator downstreamGateEvaluator,
            IHostEnvironment environment,
            IApplyAiExecutionQueue executionQueue,
            IApplyAiStageOneRuntime stageOneRuntime,
            IJobPostingPdfRenderer jobPostingPdfRenderer,
            IMatchingService matchingService,
            IS3StorageService storageService,
            IVerificationOrchestrator verificationOrchestrator,
            ILogger<ApplyAIService> logger)
        {
            _applicationGenerationService = applicationGenerationService;
            _db = db;
            _userService = userService;
            _jobStore = jobStore;
            _artifactStorage = artifactStorage;
            _candidateEvidenceService = candidateEvidenceService;
            _configuration = configuration;
            _coverLetterPdfRenderer = coverLetterPdfRenderer;
            _coverLetterTemplateRenderer = coverLetterTemplateRenderer;
            _downstreamGateEvaluator = downstreamGateEvaluator;
            _environment = environment;
            _executionQueue = executionQueue;
            _stageOneRuntime = stageOneRuntime;
            _jobPostingPdfRenderer = jobPostingPdfRenderer;
            _matchingService = matchingService;
            _storageService = storageService;
            _verificationOrchestrator = verificationOrchestrator;
            _logger = logger;
        }

        public async Task<PipelineJobAcceptedResponse> SubmitJobAsync(
            ClaimsPrincipal claimsPrincipal,
            ApplyAiJobRequest request,
            CancellationToken cancellationToken = default)
        {
            return await SubmitJobInternalAsync(claimsPrincipal, request, prepareStoredJobPostingAsync: null, cancellationToken);
        }

        public async Task<PipelineJobAcceptedResponse> SubmitUploadedJobAsync(
            ClaimsPrincipal claimsPrincipal,
            ApplyAiJobUploadRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(claimsPrincipal);
            ArgumentNullException.ThrowIfNull(request);

            if (request.JobPostingFile is null || request.JobPostingFile.Length == 0)
            {
                throw new ArgumentException("JobPostingFile is required.", nameof(request));
            }

            var pipelineRequest = new ApplyAiJobRequest
            {
                WorkflowMode = request.WorkflowMode,
                JobPostingSource = new ApplyAiJobPostingSourceRequest
                {
                    SourceType = PipelineInputKind.UploadedFile,
                    Reference = $"upload://{request.JobPostingFile.FileName}",
                    FileName = request.JobPostingFile.FileName,
                    ContentType = request.JobPostingFile.ContentType
                },
                CandidateDocuments = new ApplyAiCandidateDocumentSelection
                {
                    IncludeCurrentCv = request.IncludeCurrentCv,
                    IncludeProfileRelevantDocuments = request.IncludeProfileRelevantDocuments,
                    AdditionalFileIds = request.AdditionalFileIds,
                    IncludeAllConsentedFiles = request.IncludeAllConsentedFiles
                },
                CompanyContextOverrides = new ApplyAiCompanyContextOverrides
                {
                    CompanyName = request.CompanyName,
                    ApplicantAddressHint = request.ApplicantAddressHint
                },
                PreferencesOverride = ParseNullableJsonElement(request.PreferencesOverrideJson),
                RequestedArtifacts = ParseRequestedArtifacts(request.RequestedArtifactsJson),
                CorrelationId = request.CorrelationId
            };

            return await SubmitJobInternalAsync(
                claimsPrincipal,
                pipelineRequest,
                async (currentUser, jobId, createdAtUtc, token) =>
                {
                    await using var stream = request.JobPostingFile.OpenReadStream();
                    return await _artifactStorage.StoreJobPostingAsync(
                        jobId,
                        BuildRunStoragePrefix(currentUser.Id, createdAtUtc, jobId),
                        stream,
                        request.JobPostingFile.FileName,
                        request.JobPostingFile.ContentType ?? "application/pdf",
                        token);
                },
                cancellationToken);
        }

        public async Task<PipelineJobAcceptedResponse> SubmitLinkedJobAsync(
            ClaimsPrincipal claimsPrincipal,
            ApplyAiJobLinkRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(claimsPrincipal);
            ArgumentNullException.ThrowIfNull(request);

            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var linkUrl)
                || (linkUrl.Scheme != Uri.UriSchemeHttp && linkUrl.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentException("Url must be an absolute HTTP or HTTPS URL.", nameof(request));
            }

            var pipelineRequest = new ApplyAiJobRequest
            {
                WorkflowMode = request.WorkflowMode,
                JobPostingSource = new ApplyAiJobPostingSourceRequest
                {
                    SourceType = PipelineInputKind.RemoteUrl,
                    Reference = request.Url,
                    Url = request.Url,
                    ContentType = "application/pdf"
                },
                CandidateDocuments = new ApplyAiCandidateDocumentSelection
                {
                    IncludeCurrentCv = request.IncludeCurrentCv,
                    IncludeProfileRelevantDocuments = request.IncludeProfileRelevantDocuments,
                    AdditionalFileIds = request.AdditionalFileIds,
                    IncludeAllConsentedFiles = request.IncludeAllConsentedFiles
                },
                CompanyContextOverrides = new ApplyAiCompanyContextOverrides
                {
                    CompanyName = request.CompanyName,
                    ApplicantAddressHint = request.ApplicantAddressHint
                },
                PreferencesOverride = request.PreferencesOverride,
                RequestedArtifacts = request.RequestedArtifacts,
                CorrelationId = request.CorrelationId
            };

            return await SubmitJobInternalAsync(
                claimsPrincipal,
                pipelineRequest,
                prepareStoredJobPostingAsync: null,
                cancellationToken);
        }

        public async Task<PipelineJobSnapshot> GetJobAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            return ApplyAiSnapshotFactory.CreateSnapshot(job);
        }

        public async Task<IReadOnlyList<PipelineEventEnvelope>> GetEventsAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            return ApplyAiSnapshotFactory.CreateEvents(job);
        }

        public async Task<IReadOnlyList<PipelineArtifactReference>> GetArtifactsAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            return ApplyAiSnapshotFactory.CreateArtifacts(job);
        }

        public async Task<ApplyAiArtifactContentResponse> GetArtifactContentAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            string artifactId,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(artifactId, out var parsedArtifactId))
            {
                throw new ArgumentException("Artifact id must be a valid GUID.", nameof(artifactId));
            }

            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            var artifact = job.Artifacts.FirstOrDefault(item => item.Id == parsedArtifactId);
            if (artifact is null)
            {
                throw new KeyNotFoundException("Pipeline artifact was not found for the current user.");
            }

            if (!string.IsNullOrWhiteSpace(artifact.StorageKey))
            {
                return await _artifactStorage.DownloadArtifactAsync(artifact.StorageKey, artifact.DisplayName, artifact.MediaType, cancellationToken);
            }

            return await BuildComputedArtifactContentAsync(job, artifact, cancellationToken);
        }

        public async Task<ApplyAiPhaseDocumentResponse> GetPhaseDocumentAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            PipelinePhase phase,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            var phaseState = GetPhaseState(job, phase);
            return ApplyAiSnapshotFactory.CreatePhaseDocumentResponse(job, phaseState);
        }

        public async Task<ApplyAiPhaseDocumentResponse> UpdatePhaseDocumentAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            PipelinePhase phase,
            ApplyAiPhaseDocumentUpdateRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.DocumentJson.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new ArgumentException("DocumentJson is required.", nameof(request));
            }

            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            var phaseState = GetPhaseState(job, phase);
            EnsurePhaseDocumentCanBeUpdated(job, phaseState);

            var isManualReviewEdit = job.WorkflowMode == PipelineWorkflowMode.Manual
                && job.Status == PipelineJobStatus.AwaitingUserAction
                && job.CurrentPhase == phase;
            var now = DateTimeOffset.UtcNow;
            phaseState.DocumentJson = request.DocumentJson.GetRawText();
            phaseState.VerificationJson = BuildVerificationJson(PipelinePhaseCatalog.Get(phase), now, "pending_revalidation", request.EditorComment);
            phaseState.GateJson = BuildGateJson(now, approvedForDownstream: false, hasPendingEdits: true);
            phaseState.HasUnverifiedEdits = true;
            phaseState.ApprovedForDownstream = false;
            phaseState.Status = isManualReviewEdit
                ? PipelinePhaseStatus.AwaitingApproval
                : PipelinePhaseStatus.Completed;
            phaseState.CurrentActivity = isManualReviewEdit
                ? PipelineActivity.AwaitingUserApproval
                : PipelineActivity.Completed;
            phaseState.ApprovalRequired = isManualReviewEdit;
            phaseState.WarningCount = 0;
            phaseState.ErrorCount = 0;
            phaseState.CompletedAtUtc ??= now;
            phaseState.StatusMessage = isManualReviewEdit
                ? $"{PipelinePhaseCatalog.Get(phase).DisplayName}: awaiting manual approval after edits."
                : $"{PipelinePhaseCatalog.Get(phase).DisplayName}: updated. Retry the downstream phase to apply the reviewed document.";

            job.UpdatedAtUtc = now;
            phaseState.DocumentId ??= BuildDocumentId(job, phase, Math.Max(phaseState.AttemptCount, 1));
            RebuildPhaseArtifacts(job, phase, phaseState.DocumentId);
            await PersistPhaseArtifactsAsync(job, phase, cancellationToken);
            AppendEvent(
                job,
                PipelineEventType.JobProgressUpdated,
                phase,
                phaseState.CurrentActivity ?? PipelineActivity.Completed,
                phaseState.StatusMessage,
                now);
            await _jobStore.SaveChangesAsync(cancellationToken);

            return ApplyAiSnapshotFactory.CreatePhaseDocumentResponse(job, phaseState);
        }

        public async Task<PipelineJobSnapshot> ApprovePhaseAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            PipelinePhase phase,
            ApplyAiPhaseApprovalRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            EnsureEditablePhase(job, phase);

            var phaseState = GetPhaseState(job, phase);
            var now = DateTimeOffset.UtcNow;
            if (phaseState.HasUnverifiedEdits)
            {
                phaseState.VerificationJson = BuildVerificationJson(PipelinePhaseCatalog.Get(phase), now, "pass", request?.Comment);
                phaseState.GateJson = BuildGateJson(now, approvedForDownstream: true, hasPendingEdits: false);
                phaseState.HasUnverifiedEdits = false;
            }

            phaseState.Status = PipelinePhaseStatus.Completed;
            phaseState.CurrentActivity = PipelineActivity.Completed;
            phaseState.ApprovalRequired = false;
            phaseState.ApprovedForDownstream = true;
            phaseState.ApprovedAtUtc = now;
            phaseState.CompletedAtUtc ??= now;
            phaseState.StatusMessage = string.IsNullOrWhiteSpace(request?.Comment)
                ? "Approved for downstream execution."
                : $"Approved for downstream execution. Comment: {request!.Comment}";

            await PersistPhaseArtifactsAsync(job, phase, cancellationToken);

            var nextPhase = PipelinePhaseCatalog.GetNext(phase);
            if (nextPhase is null)
            {
                MarkJobCompleted(job, now, "Pipeline completed.");
                AppendEvent(job, PipelineEventType.JobCompleted, phase, PipelineActivity.Completed, job.StatusMessage, now);
            }
            else
            {
                job.Status = PipelineJobStatus.Running;
                job.CurrentPhase = nextPhase;
                job.CurrentActivity = PipelineActivity.Queued;
                job.StatusMessage = $"{PipelinePhaseCatalog.Get(nextPhase.Value).DisplayName}: queued after manual approval.";
                job.UpdatedAtUtc = now;
                job.ProgressPercent = CalculateProgressPercent(job);

                AppendEvent(job, PipelineEventType.JobProgressUpdated, nextPhase, PipelineActivity.Queued, job.StatusMessage, now);
                await ExecuteUntilBlockedOrCompletedAsync(job, cancellationToken);
            }

            await _jobStore.SaveChangesAsync(cancellationToken);
            return ApplyAiSnapshotFactory.CreateSnapshot(job);
        }

        public async Task<PipelineJobSnapshot> RetryPhaseAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            PipelinePhase phase,
            ApplyAiPhaseRetryRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            var job = await GetOwnedJobAsync(claimsPrincipal, jobId, cancellationToken);
            EnsurePhaseCanBeRetried(job, phase);

            ApplyRetryOverrides(job, phase, request);

            InvalidatePhaseAndDownstream(job, phase);

            var phaseState = GetPhaseState(job, phase);

            var now = DateTimeOffset.UtcNow;
            job.Status = PipelineJobStatus.Running;
            job.CurrentPhase = phase;
            job.CurrentActivity = PipelineActivity.Queued;
            job.StatusMessage = string.IsNullOrWhiteSpace(request?.Reason)
                ? $"{PipelinePhaseCatalog.Get(phase).DisplayName}: retry queued."
                : $"{PipelinePhaseCatalog.Get(phase).DisplayName}: retry queued. Reason: {request!.Reason}";
            job.UpdatedAtUtc = now;
            job.CompletedAtUtc = null;
            AppendEvent(job, PipelineEventType.JobProgressUpdated, phase, PipelineActivity.Queued, job.StatusMessage, now);

            await ExecutePhaseAsync(job, phase, isRetry: true, cancellationToken);
            await ExecuteUntilBlockedOrCompletedAsync(job, cancellationToken);

            await _jobStore.SaveChangesAsync(cancellationToken);
            return ApplyAiSnapshotFactory.CreateSnapshot(job);
        }

        private async Task<PipelineJobAcceptedResponse> SubmitJobInternalAsync(
            ClaimsPrincipal claimsPrincipal,
            ApplyAiJobRequest request,
            Func<User, Guid, DateTimeOffset, CancellationToken, Task<ApplyAiStoredArtifact>>? prepareStoredJobPostingAsync,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(claimsPrincipal);
            ArgumentNullException.ThrowIfNull(request);

            var currentUser = await _userService.GetUser(claimsPrincipal);
            var resolvedUserContext = await ResolveUserContextAsync(currentUser.Id, request.CandidateDocuments, cancellationToken);

            ValidateResolvedUserContext(request, resolvedUserContext);

            var createdAtUtc = DateTimeOffset.UtcNow;
            var jobId = Guid.NewGuid();
            ApplyAiStoredArtifact? storedJobPosting = null;

            if (prepareStoredJobPostingAsync is not null)
            {
                storedJobPosting = await prepareStoredJobPostingAsync(currentUser, jobId, createdAtUtc, cancellationToken);
            }

            var job = CreateJob(currentUser, request, resolvedUserContext, createdAtUtc, jobId, storedJobPosting);
            _jobStore.Add(job);
            await _jobStore.SaveChangesAsync(cancellationToken);
            await _executionQueue.QueueAsync(job.Id, cancellationToken);

            _logger.LogInformation(
                "ApplyAI pipeline job {JobId} persisted and queued for user {UserId} with workflow {WorkflowMode}.",
                job.Id,
                currentUser.Id,
                request.WorkflowMode);

            return ApplyAiSnapshotFactory.CreateAcceptedResponse(job);
        }

        public async Task ExecuteQueuedJobAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var job = await _jobStore.GetAsync(jobId, cancellationToken);
            if (job.Status is PipelineJobStatus.Completed or PipelineJobStatus.Failed)
            {
                return;
            }

            try
            {
                await EnsureStoredJobPostingAsync(job, cancellationToken);
                await ExecuteUntilBlockedOrCompletedAsync(job, cancellationToken);
                await _jobStore.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                try
                {
                    await MarkQueuedJobFailedAsync(job, exception.Message, cancellationToken);
                }
                catch (Exception markFailureException)
                {
                    _logger.LogError(markFailureException, "ApplyAI queued execution could not persist the failed state for job {JobId}.", jobId);
                }

                _logger.LogError(exception, "ApplyAI queued execution failed for job {JobId}.", jobId);
            }
        }

        private static void ValidateResolvedUserContext(ApplyAiJobRequest request, ApplyAiResolvedUserContext resolvedUserContext)
        {
            if (!resolvedUserContext.HasAcceptedActiveTerms)
            {
                throw new InvalidOperationException("missing_active_terms_consent: The current active terms must be accepted before starting an ApplyAI job.");
            }

            if (resolvedUserContext.CandidateFiles.Length == 0)
            {
                throw new InvalidOperationException("missing_candidate_documents: No consented candidate documents were available for the pipeline job.");
            }

            if (!HasPreferences(request.PreferencesOverride))
            {
                throw new InvalidOperationException("missing_preferences: preferencesOverride is required for the test implementation because it always runs application_generation.");
            }
        }

        private ApplyAiPipelineJob CreateJob(
            User currentUser,
            ApplyAiJobRequest request,
            ApplyAiResolvedUserContext resolvedUserContext,
            DateTimeOffset createdAtUtc,
            Guid jobId,
            ApplyAiStoredArtifact? storedJobPosting)
        {
            PipelineSubmissionValidator.EnsureValid(BuildSubmissionRequest(currentUser, request));
            var runStoragePrefix = StoragePathBuilder.BuildRunStoragePrefix(currentUser.Id, createdAtUtc, jobId);

            var job = new ApplyAiPipelineJob
            {
                Id = jobId,
                UserId = currentUser.Id,
                WorkflowMode = request.WorkflowMode,
                Status = PipelineJobStatus.Queued,
                CurrentActivity = PipelineActivity.Queued,
                StatusMessage = "Job accepted. Waiting for execution.",
                ProgressPercent = 0,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = createdAtUtc,
                CorrelationId = request.CorrelationId,
                JobPostingSourceType = request.JobPostingSource.SourceType,
                JobPostingReference = request.JobPostingSource.ResolveReference(),
                JobPostingOriginalFileName = request.JobPostingSource.FileName,
                JobPostingContentType = request.JobPostingSource.ContentType,
                IncludeCurrentCv = request.CandidateDocuments.IncludeCurrentCv,
                IncludeProfileRelevantDocuments = request.CandidateDocuments.IncludeProfileRelevantDocuments,
                IncludeAllConsentedFiles = request.CandidateDocuments.IncludeAllConsentedFiles,
                SelectedFileIdsJson = JsonSerializer.Serialize(resolvedUserContext.CandidateFiles.Select(item => item.FileId).ToArray()),
                CandidateFileSnapshotJson = JsonSerializer.Serialize(resolvedUserContext.CandidateFiles),
                RequestedArtifactsJson = JsonSerializer.Serialize(request.RequestedArtifacts),
                PreferencesSnapshotJson = SerializeNullableElement(request.PreferencesOverride),
                CompanyNameOverride = request.CompanyContextOverrides.CompanyName,
                ApplicantAddressHint = request.CompanyContextOverrides.ApplicantAddressHint,
                RunStoragePrefix = runStoragePrefix,
                DisplayRunName = $"ApplyAI {createdAtUtc:yyyy-MM-dd HH:mm} {jobId:N}"
            };

            if (storedJobPosting is not null)
            {
                AddArtifact(job, CreateStoredJobPostingArtifact(job, storedJobPosting));
            }

            foreach (var definition in PipelinePhaseCatalog.All)
            {
                job.PhaseStates.Add(new ApplyAiPipelinePhaseState
                {
                    Id = Guid.NewGuid(),
                    JobId = jobId,
                    Job = job,
                    Phase = definition.Phase,
                    Status = PipelinePhaseStatus.Pending,
                    StatusMessage = $"{definition.DisplayName}: pending."
                });
            }

            AppendEvent(job, PipelineEventType.JobAccepted, null, PipelineActivity.Queued, job.StatusMessage, createdAtUtc);
            return job;
        }

        private PipelineSubmissionRequest BuildSubmissionRequest(User currentUser, ApplyAiJobRequest request)
        {
            return new PipelineSubmissionRequest(
                currentUser.Id.ToString(),
                request.WorkflowMode,
                new PipelineSourceReference(
                    request.JobPostingSource.SourceType,
                    request.JobPostingSource.ResolveReference(),
                    request.JobPostingSource.FileName,
                    request.JobPostingSource.ContentType),
                new PipelineRequestedArtifacts(
                    request.RequestedArtifacts.IncludeCoverLetter,
                    request.RequestedArtifacts.IncludeFitAdvisory),
                request.CorrelationId);
        }

        private static ApplyAiPipelineArtifact CreateStoredJobPostingArtifact(ApplyAiPipelineJob job, ApplyAiStoredArtifact storedJobPosting)
        {
            return new ApplyAiPipelineArtifact
            {
                Id = storedJobPosting.ArtifactId,
                JobId = job.Id,
                Job = job,
                ArtifactKind = PipelineArtifactKind.PdfDocument,
                RelativePath = storedJobPosting.RelativePath,
                StorageKey = storedJobPosting.StorageKey,
                DisplayName = storedJobPosting.DisplayName,
                MediaType = storedJobPosting.MediaType,
                IsPrimary = true,
            };
        }

        private async Task<ApplyAiPipelineJob> GetOwnedJobAsync(
            ClaimsPrincipal claimsPrincipal,
            string jobId,
            CancellationToken cancellationToken)
        {
            var currentUser = await _userService.GetUser(claimsPrincipal);
            return await _jobStore.GetOwnedAsync(jobId, currentUser.Id, cancellationToken);
        }

        private async Task<ApplyAiResolvedUserContext> ResolveUserContextAsync(
            Guid userId,
            ApplyAiCandidateDocumentSelection selection,
            CancellationToken cancellationToken)
        {
            selection ??= new ApplyAiCandidateDocumentSelection();

            var profile = await _db.Profiles
                .AsNoTracking()
                .Include(item => item.CurrentCv)
                .Include(item => item.RelevantDocuments)
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            var activeTermIds = await _db.Term
                .AsNoTracking()
                .Select(term => term.Id)
                .ToArrayAsync(cancellationToken);

            var selectedFiles = new Dictionary<Guid, S3File>();

            if (selection.IncludeCurrentCv && profile?.CurrentCv is not null)
            {
                selectedFiles[profile.CurrentCv.Id] = profile.CurrentCv;
            }

            if (selection.IncludeProfileRelevantDocuments && profile?.RelevantDocuments is not null)
            {
                foreach (var relevantDocument in profile.RelevantDocuments)
                {
                    selectedFiles[relevantDocument.Id] = relevantDocument;
                }
            }

            var requestedAdditionalIds = selection.AdditionalFileIds
                .Where(fileId => fileId != Guid.Empty)
                .Distinct()
                .ToArray();

            if (requestedAdditionalIds.Length > 0)
            {
                var additionalFiles = await _db.S3Files
                    .AsNoTracking()
                    .Where(file => file.UserId == userId && requestedAdditionalIds.Contains(file.Id) && !activeTermIds.Contains(file.Id))
                    .ToArrayAsync(cancellationToken);

                var foundIds = additionalFiles.Select(file => file.Id).ToHashSet();
                var missingIds = requestedAdditionalIds.Where(fileId => !foundIds.Contains(fileId)).ToArray();
                if (missingIds.Length > 0)
                {
                    throw new InvalidOperationException($"missing_candidate_documents: One or more selected files are not owned by the current user or are not eligible for ApplyAI processing. Missing ids: {string.Join(", ", missingIds)}");
                }

                foreach (var additionalFile in additionalFiles)
                {
                    selectedFiles[additionalFile.Id] = additionalFile;
                }
            }

            if (selection.IncludeAllConsentedFiles)
            {
                var consentedFiles = await _db.Consents
                    .AsNoTracking()
                    .Include(consent => consent.File)
                    .Where(consent => consent.UserId == userId && !consent.ConsentRetracted && !activeTermIds.Contains(consent.FileId))
                    .Select(consent => consent.File)
                    .ToArrayAsync(cancellationToken);

                foreach (var consentedFile in consentedFiles)
                {
                    selectedFiles[consentedFile.Id] = consentedFile;
                }
            }

            var selectedFileIds = selectedFiles.Keys.ToArray();
            if (selectedFileIds.Length > 0)
            {
                var consentedIds = await _db.Consents
                    .AsNoTracking()
                    .Where(consent => consent.UserId == userId && !consent.ConsentRetracted && selectedFileIds.Contains(consent.FileId))
                    .Select(consent => consent.FileId)
                    .ToArrayAsync(cancellationToken);

                var consentedLookup = consentedIds.ToHashSet();
                var nonConsentedFiles = selectedFiles.Values
                    .Where(file => !consentedLookup.Contains(file.Id))
                    .Select(file => file.FileName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (nonConsentedFiles.Length > 0)
                {
                    throw new InvalidOperationException($"missing_file_consent: One or more selected files do not have active consent. Files: {string.Join(", ", nonConsentedFiles)}");
                }
            }

            var activeTermId = await _db.Term
                .AsNoTracking()
                .Where(term => term.Active)
                .Select(term => (Guid?)term.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var hasAcceptedActiveTerms = activeTermId is null || await _db.Consents
                .AsNoTracking()
                .AnyAsync(consent => consent.UserId == userId && consent.FileId == activeTermId.Value && !consent.ConsentRetracted, cancellationToken);

            return new ApplyAiResolvedUserContext(
                userId,
                profile?.Id,
                selectedFiles.Values
                    .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(file => new ApplyAiCandidateFileSummary(file.Id, file.FileName, DateTime.SpecifyKind(file.UploadTime, DateTimeKind.Utc)))
                    .ToArray(),
                hasAcceptedActiveTerms);
        }

        private async Task ExecuteUntilBlockedOrCompletedAsync(ApplyAiPipelineJob job, CancellationToken cancellationToken)
        {
            while (job.Status is not PipelineJobStatus.Completed and not PipelineJobStatus.Failed and not PipelineJobStatus.AwaitingUserAction)
            {
                var nextPhase = job.CurrentPhase ?? job.PhaseStates
                    .OrderBy(item => PipelinePhaseCatalog.IndexOf(item.Phase))
                    .FirstOrDefault(item => item.Status == PipelinePhaseStatus.Pending)
                    ?.Phase;

                if (!nextPhase.HasValue)
                {
                    MarkJobCompleted(job, DateTimeOffset.UtcNow, "Pipeline completed.");
                    AppendEvent(job, PipelineEventType.JobCompleted, null, PipelineActivity.Completed, job.StatusMessage, job.CompletedAtUtc ?? DateTimeOffset.UtcNow);
                    return;
                }

                await ExecutePhaseAsync(job, nextPhase.Value, isRetry: false, cancellationToken);
            }
        }

        private async Task ExecutePhaseAsync(ApplyAiPipelineJob job, PipelinePhase phase, bool isRetry, CancellationToken cancellationToken)
        {
            var phaseState = GetPhaseState(job, phase);
            var definition = PipelinePhaseCatalog.Get(phase);
            var startedAtUtc = DateTimeOffset.UtcNow;

            phaseState.Status = PipelinePhaseStatus.Running;
            phaseState.CurrentActivity = isRetry ? PipelineActivity.RunningRepair : PipelineActivity.PreparingPrompt;
            phaseState.StartedAtUtc ??= startedAtUtc;
            phaseState.CompletedAtUtc = null;
            phaseState.ApprovedAtUtc = null;
            phaseState.ApprovalRequired = false;
            phaseState.HasUnverifiedEdits = false;
            phaseState.ApprovedForDownstream = false;
            phaseState.WarningCount = 0;
            phaseState.ErrorCount = 0;
            phaseState.AttemptCount++;
            if (isRetry)
            {
                phaseState.RepairAttemptCount++;
            }

            job.Status = PipelineJobStatus.Running;
            job.CurrentPhase = phase;
            job.CurrentActivity = phaseState.CurrentActivity.Value;
            job.StatusMessage = BuildUserFacingPhaseStatusMessage(definition);
            job.UpdatedAtUtc = startedAtUtc;

            AppendEvent(
                job,
                phaseState.AttemptCount == 1 && !isRetry ? PipelineEventType.PhaseStarted : PipelineEventType.JobProgressUpdated,
                phase,
                job.CurrentActivity,
                job.StatusMessage,
                startedAtUtc);
            await _jobStore.SaveChangesAsync(cancellationToken);

            PhaseExecutionOutcome outcome;
            try
            {
                outcome = phase switch
                {
                    PipelinePhase.CompanyContext => await ExecuteCompanyContextPhaseAsync(job, definition, phaseState, isRetry, cancellationToken),
                    PipelinePhase.CandidateEvidence => await ExecuteCandidateEvidencePhaseAsync(job, definition, phaseState, isRetry, cancellationToken),
                    PipelinePhase.ApplicationGeneration => await ExecuteApplicationGenerationPhaseAsync(job, definition, phaseState, isRetry, cancellationToken),
                    PipelinePhase.Matching => await ExecuteMatchingPhaseAsync(job, definition, phaseState, isRetry, cancellationToken),
                    PipelinePhase.Requirements => await ExecuteRequirementsPhaseAsync(job, definition, phaseState, isRetry, cancellationToken),
                    _ => ExecuteSyntheticPhase(job, definition, phaseState, isRetry, startedAtUtc),
                };
            }
            catch (Exception exception)
            {
                var failedAtUtc = DateTimeOffset.UtcNow;
                phaseState.Status = PipelinePhaseStatus.Failed;
                phaseState.CurrentActivity = PipelineActivity.Failed;
                phaseState.CompletedAtUtc = failedAtUtc;
                phaseState.StatusMessage = $"{definition.DisplayName}: failed. {exception.Message}";
                phaseState.WarningCount = 0;
                phaseState.ErrorCount = 1;
                phaseState.VerificationJson = JsonSerializer.Serialize(new
                {
                    status = "failed",
                    message = exception.Message,
                    failedAtUtc,
                });
                phaseState.GateJson = JsonSerializer.Serialize(new
                {
                    approvedForDownstream = false,
                    recommendedAction = "repair_or_regenerate",
                    checkedAtUtc = failedAtUtc,
                });

                job.Status = PipelineJobStatus.Failed;
                job.CurrentPhase = phase;
                job.CurrentActivity = PipelineActivity.Failed;
                job.StatusMessage = phaseState.StatusMessage;
                job.UpdatedAtUtc = failedAtUtc;
                job.ProgressPercent = CalculateProgressPercent(job);

                AppendEvent(job, PipelineEventType.JobFailed, phase, PipelineActivity.Failed, job.StatusMessage, failedAtUtc);
                await _jobStore.SaveChangesAsync(cancellationToken);
                return;
            }

            var completedAtUtc = outcome.CompletedAtUtc;
            phaseState.DocumentId = BuildDocumentId(job, phase, phaseState.AttemptCount);
            phaseState.DocumentJson = outcome.DocumentJson;
            phaseState.VerificationJson = outcome.VerificationJson;
            phaseState.GateJson = outcome.GateJson;
            phaseState.WarningCount = outcome.WarningCount;
            phaseState.ErrorCount = outcome.ErrorCount;

            var nextPhase = PipelinePhaseCatalog.GetNext(phase);
            var requiresApproval = job.WorkflowMode == PipelineWorkflowMode.Manual && nextPhase.HasValue;

            RebuildPhaseArtifacts(job, phase, phaseState.DocumentId);

            if (!outcome.ApprovedForDownstream)
            {
                phaseState.Status = PipelinePhaseStatus.Failed;
                phaseState.CurrentActivity = PipelineActivity.Failed;
                phaseState.ApprovalRequired = false;
                phaseState.ApprovedForDownstream = false;
                phaseState.StatusMessage = outcome.CompletionMessage;

                job.Status = PipelineJobStatus.Failed;
                job.CurrentPhase = phase;
                job.CurrentActivity = PipelineActivity.Failed;
                job.StatusMessage = outcome.CompletionMessage;
                job.UpdatedAtUtc = completedAtUtc;
                job.ProgressPercent = CalculateProgressPercent(job);

                AppendEvent(job, PipelineEventType.JobFailed, phase, PipelineActivity.Failed, job.StatusMessage, completedAtUtc);
                await PersistPhaseArtifactsAsync(job, phase, cancellationToken);
                await _jobStore.SaveChangesAsync(cancellationToken);
                return;
            }

            phaseState.CompletedAtUtc = completedAtUtc;
            phaseState.CurrentActivity = PipelineActivity.Completed;
            if (requiresApproval)
            {
                phaseState.Status = PipelinePhaseStatus.AwaitingApproval;
                phaseState.ApprovalRequired = true;
                phaseState.ApprovedForDownstream = false;
                phaseState.GateJson = BuildGateJson(completedAtUtc, approvedForDownstream: false, hasPendingEdits: false);
                phaseState.StatusMessage = $"{definition.DisplayName}: ready for manual review.";

                job.Status = PipelineJobStatus.AwaitingUserAction;
                job.CurrentPhase = phase;
                job.CurrentActivity = PipelineActivity.AwaitingUserApproval;
                job.StatusMessage = phaseState.StatusMessage;
                job.UpdatedAtUtc = completedAtUtc;
                job.ProgressPercent = CalculateProgressPercent(job);

                AppendEvent(job, PipelineEventType.ApprovalRequired, phase, job.CurrentActivity, job.StatusMessage, completedAtUtc);
                await PersistPhaseArtifactsAsync(job, phase, cancellationToken);
                await _jobStore.SaveChangesAsync(cancellationToken);
                return;
            }

            phaseState.Status = PipelinePhaseStatus.Completed;
            phaseState.ApprovalRequired = false;
            phaseState.ApprovedForDownstream = true;
            phaseState.StatusMessage = outcome.CompletionMessage;

            if (nextPhase is null)
            {
                MarkJobCompleted(job, completedAtUtc, "Pipeline completed.");
                AppendEvent(job, PipelineEventType.JobCompleted, phase, PipelineActivity.Completed, job.StatusMessage, completedAtUtc);
                await PersistPhaseArtifactsAsync(job, phase, cancellationToken);
                await _jobStore.SaveChangesAsync(cancellationToken);
                return;
            }

            job.Status = PipelineJobStatus.Running;
            job.CurrentPhase = nextPhase;
            job.CurrentActivity = PipelineActivity.Queued;
            job.StatusMessage = BuildUserFacingPhaseStatusMessage(PipelinePhaseCatalog.Get(nextPhase.Value));
            job.UpdatedAtUtc = completedAtUtc;
            job.ProgressPercent = CalculateProgressPercent(job);

            AppendEvent(job, PipelineEventType.PhaseCompleted, phase, PipelineActivity.Completed, phaseState.StatusMessage, completedAtUtc);
            await PersistPhaseArtifactsAsync(job, phase, cancellationToken);
            await _jobStore.SaveChangesAsync(cancellationToken);
        }

        private async Task<PhaseExecutionOutcome> ExecuteCompanyContextPhaseAsync(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            await UpdatePhaseProgressAsync(
                job,
                phaseState,
                PipelineActivity.PreparingPrompt,
                "Performing Company Context Research",
                cancellationToken);

            var jobPosting = await GetStoredJobPostingContentAsync(job, cancellationToken);
            var applicantProfileText = await BuildApplicantProfileTextAsync(job.UserId, cancellationToken);

            await UpdatePhaseProgressAsync(
                job,
                phaseState,
                PipelineActivity.WaitingForLlmResponse,
                "Performing Company Context Research",
                cancellationToken);

            var outputJson = await _stageOneRuntime.GenerateCompanyContextAsync(
                jobPosting.Content,
                jobPosting.FileName,
                jobPosting.MediaType,
                job.CompanyNameOverride,
                applicantProfileText,
                job.ApplicantAddressHint,
                cancellationToken);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var wrappedDocument = BuildPhaseDocumentJson(
                job,
                definition,
                phaseState.AttemptCount,
                completedAtUtc,
                isRetry,
                JsonNode.Parse(outputJson));

            return new PhaseExecutionOutcome(
                wrappedDocument,
                BuildVerificationJson(definition, completedAtUtc, "not_applicable"),
                BuildGateJson(completedAtUtc, approvedForDownstream: true, hasPendingEdits: false),
                0,
                0,
                true,
                $"{definition.DisplayName}: completed through the in-process ApplyAI runtime.",
                completedAtUtc);
        }

        private async Task<PhaseExecutionOutcome> ExecuteRequirementsPhaseAsync(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            await UpdatePhaseProgressAsync(
                job,
                phaseState,
                PipelineActivity.PreparingPrompt,
                "Preparing requirement analysis",
                cancellationToken);

            var jobPosting = await GetStoredJobPostingContentAsync(job, cancellationToken);

            await UpdatePhaseProgressAsync(
                job,
                phaseState,
                PipelineActivity.WaitingForLlmResponse,
                "Performing requirement analysis",
                cancellationToken);

            var outputJson = await _stageOneRuntime.GenerateRequirementsAsync(
                jobPosting.Content,
                jobPosting.FileName,
                jobPosting.MediaType,
                cancellationToken);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var documentId = BuildDocumentId(job, PipelinePhase.Requirements, phaseState.AttemptCount);
            await UpdatePhaseProgressAsync(
                job,
                phaseState,
                PipelineActivity.RunningVerification,
                "Verifying requirement analysis",
                cancellationToken);
            var verificationResult = await _stageOneRuntime.VerifyRequirementsAsync(
                documentId,
                outputJson,
                jobPosting.FileName,
                cancellationToken);

            var wrappedDocument = BuildPhaseDocumentJson(
                job,
                definition,
                phaseState.AttemptCount,
                completedAtUtc,
                isRetry,
                JsonNode.Parse(outputJson));

            var completionMessage = verificationResult.ApprovedForDownstream
                ? $"{definition.DisplayName}: completed through the in-process ApplyAI runtime."
                : $"{definition.DisplayName}: blocked by verification or downstream gate.";

            return new PhaseExecutionOutcome(
                wrappedDocument,
                verificationResult.VerificationJson,
                verificationResult.GateJson,
                verificationResult.WarningCount,
                verificationResult.ErrorCount,
                verificationResult.ApprovedForDownstream,
                completionMessage,
                completedAtUtc);
        }

        private async Task<PhaseExecutionOutcome> ExecuteCandidateEvidencePhaseAsync(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            var requirementsPhaseState = GetPhaseState(job, PipelinePhase.Requirements);
            var requirementsDocumentId = GetRequiredDocumentId(requirementsPhaseState, PipelinePhase.Requirements);
            var requirementsJson = ExtractPhaseOutputJson(requirementsPhaseState);

            phaseState.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            job.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            phaseState.StatusMessage = $"{definition.DisplayName}: sending requirements and candidate files to the in-process ApplyAI runtime.";
            job.StatusMessage = phaseState.StatusMessage;

            var candidateFiles = await MaterializeCandidateFilesAsync(job, cancellationToken);
            try
            {
                phaseState.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
                job.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
                phaseState.StatusMessage = $"{definition.DisplayName}: waiting for OpenAI response.";
                job.StatusMessage = phaseState.StatusMessage;

                var outputJson = await _candidateEvidenceService.GenerateCandidateEvidenceAsync(
                    new CandidateEvidenceGenerationRequest
                    {
                        RequirementsDocumentId = requirementsDocumentId,
                        RequirementsDocumentJson = requirementsJson,
                        CandidateFilePaths = candidateFiles.FilePaths
                    },
                    cancellationToken);

                var completedAtUtc = DateTimeOffset.UtcNow;
                var documentId = BuildDocumentId(job, PipelinePhase.CandidateEvidence, phaseState.AttemptCount);
                var verificationRequest = new StageVerificationRequest
                {
                    Stage = VerificationStage.CandidateEvidence,
                    DocumentId = documentId,
                    DocumentJson = outputJson.OutputJson,
                    OutputSchemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(_configuration, _environment, definition.OutputSchemaPath),
                    ExpectedParsedFiles = candidateFiles.FileNames.ToList(),
                    AllowedCitationFiles = candidateFiles.FileNames.ToList(),
                    DisallowedCitationFiles = BuildDisallowedJobPostingFileNames(job),
                    RequirementsDocumentJson = requirementsJson,
                    ExpectedRequirementsDocumentId = requirementsDocumentId
                };

                var verificationResult = await ExecuteMechanicalVerificationAsync(verificationRequest, cancellationToken);
                var wrappedDocument = BuildPhaseDocumentJson(
                    job,
                    definition,
                    phaseState.AttemptCount,
                    completedAtUtc,
                    isRetry,
                    JsonNode.Parse(outputJson.OutputJson),
                    outputJson);

                var completionMessage = verificationResult.ApprovedForDownstream
                    ? $"{definition.DisplayName}: completed through the in-process ApplyAI runtime."
                    : $"{definition.DisplayName}: blocked by verification or downstream gate.";

                return new PhaseExecutionOutcome(
                    wrappedDocument,
                    verificationResult.VerificationJson,
                    verificationResult.GateJson,
                    verificationResult.WarningCount,
                    verificationResult.ErrorCount,
                    verificationResult.ApprovedForDownstream,
                    completionMessage,
                    completedAtUtc);
            }
            finally
            {
                DeleteTemporaryWorkingDirectory(candidateFiles.TempDirectory);
            }
        }

        private async Task<PhaseExecutionOutcome> ExecuteMatchingPhaseAsync(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            var requirementsPhaseState = GetPhaseState(job, PipelinePhase.Requirements);
            var candidateEvidencePhaseState = GetPhaseState(job, PipelinePhase.CandidateEvidence);
            var requirementsDocumentId = GetRequiredDocumentId(requirementsPhaseState, PipelinePhase.Requirements);
            var candidateEvidenceDocumentId = GetRequiredDocumentId(candidateEvidencePhaseState, PipelinePhase.CandidateEvidence);
            var requirementsJson = ExtractPhaseOutputJson(requirementsPhaseState);
            var candidateEvidenceJson = ExtractPhaseOutputJson(candidateEvidencePhaseState);

            phaseState.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            job.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            phaseState.StatusMessage = $"{definition.DisplayName}: sending requirements and candidate evidence to the in-process ApplyAI runtime.";
            job.StatusMessage = phaseState.StatusMessage;

            phaseState.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
            job.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
            phaseState.StatusMessage = $"{definition.DisplayName}: waiting for OpenAI response.";
            job.StatusMessage = phaseState.StatusMessage;

            var outputJson = await _matchingService.GenerateMatchingAsync(
                new MatchingGenerationRequest
                {
                    RequirementsDocumentId = requirementsDocumentId,
                    RequirementsDocumentJson = requirementsJson,
                    CandidateEvidenceDocumentId = candidateEvidenceDocumentId,
                    CandidateEvidenceDocumentJson = candidateEvidenceJson
                },
                cancellationToken);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var documentId = BuildDocumentId(job, PipelinePhase.Matching, phaseState.AttemptCount);
            var verificationRequest = new StageVerificationRequest
            {
                Stage = VerificationStage.Matching,
                DocumentId = documentId,
                DocumentJson = outputJson.OutputJson,
                OutputSchemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(_configuration, _environment, definition.OutputSchemaPath),
                RequirementsDocumentJson = requirementsJson,
                ExpectedRequirementsDocumentId = requirementsDocumentId,
                CandidateEvidenceDocumentJson = candidateEvidenceJson,
                ExpectedCandidateEvidenceDocumentId = candidateEvidenceDocumentId
            };

            var verificationResult = await ExecuteMechanicalVerificationAsync(verificationRequest, cancellationToken);
            var wrappedDocument = BuildPhaseDocumentJson(
                job,
                definition,
                phaseState.AttemptCount,
                completedAtUtc,
                isRetry,
                JsonNode.Parse(outputJson.OutputJson),
                outputJson);

            var completionMessage = verificationResult.ApprovedForDownstream
                ? $"{definition.DisplayName}: completed through the in-process ApplyAI runtime."
                : $"{definition.DisplayName}: blocked by verification or downstream gate.";

            return new PhaseExecutionOutcome(
                wrappedDocument,
                verificationResult.VerificationJson,
                verificationResult.GateJson,
                verificationResult.WarningCount,
                verificationResult.ErrorCount,
                verificationResult.ApprovedForDownstream,
                completionMessage,
                completedAtUtc);
        }

        private async Task<PhaseExecutionOutcome> ExecuteApplicationGenerationPhaseAsync(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(job.PreferencesSnapshotJson))
            {
                throw new InvalidOperationException("Application generation cannot run because the pipeline job does not contain a stored preferences snapshot.");
            }

            var requirementsPhaseState = GetPhaseState(job, PipelinePhase.Requirements);
            var candidateEvidencePhaseState = GetPhaseState(job, PipelinePhase.CandidateEvidence);
            var companyContextPhaseState = GetPhaseState(job, PipelinePhase.CompanyContext);
            var matchingPhaseState = GetPhaseState(job, PipelinePhase.Matching);
            var documentId = BuildDocumentId(job, PipelinePhase.ApplicationGeneration, phaseState.AttemptCount);

            var requirementsDocumentId = GetRequiredDocumentId(requirementsPhaseState, PipelinePhase.Requirements);
            var candidateEvidenceDocumentId = GetRequiredDocumentId(candidateEvidencePhaseState, PipelinePhase.CandidateEvidence);
            var companyContextDocumentId = GetRequiredDocumentId(companyContextPhaseState, PipelinePhase.CompanyContext);
            var matchingDocumentId = GetRequiredDocumentId(matchingPhaseState, PipelinePhase.Matching);
            var requirementsJson = ExtractPhaseOutputJson(requirementsPhaseState);
            var candidateEvidenceJson = ExtractPhaseOutputJson(candidateEvidencePhaseState);
            var companyContextJson = ExtractPhaseOutputJson(companyContextPhaseState);
            var matchingJson = ExtractPhaseOutputJson(matchingPhaseState);

            phaseState.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            job.CurrentActivity = PipelineActivity.SendingTaskToLlm;
            phaseState.StatusMessage = $"{definition.DisplayName}: sending upstream documents and preferences to the in-process ApplyAI runtime.";
            job.StatusMessage = phaseState.StatusMessage;

            phaseState.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
            job.CurrentActivity = PipelineActivity.WaitingForLlmResponse;
            phaseState.StatusMessage = $"{definition.DisplayName}: waiting for OpenAI response.";
            job.StatusMessage = phaseState.StatusMessage;

            var outputJson = await _applicationGenerationService.GenerateApplicationGenerationAsync(
                new ApplicationGenerationRequest
                {
                    ApplicationDocumentId = documentId,
                    RequirementsDocumentId = requirementsDocumentId,
                    RequirementsDocumentJson = requirementsJson,
                    CandidateEvidenceDocumentId = candidateEvidenceDocumentId,
                    CandidateEvidenceDocumentJson = candidateEvidenceJson,
                    CompanyContextDocumentId = companyContextDocumentId,
                    CompanyContextDocumentJson = companyContextJson,
                    MatchingDocumentId = matchingDocumentId,
                    MatchingDocumentJson = matchingJson,
                    PreferencesJson = job.PreferencesSnapshotJson
                },
                cancellationToken);

            var completedAtUtc = DateTimeOffset.UtcNow;
            var verificationRequest = new StageVerificationRequest
            {
                Stage = VerificationStage.ApplicationGeneration,
                DocumentId = documentId,
                DocumentJson = outputJson.OutputJson,
                OutputSchemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(_configuration, _environment, definition.OutputSchemaPath),
                RequirementsDocumentJson = requirementsJson,
                CandidateEvidenceDocumentJson = candidateEvidenceJson,
                MatchingDocumentJson = matchingJson,
                ExpectedRequirementsDocumentId = requirementsDocumentId,
                ExpectedCandidateEvidenceDocumentId = candidateEvidenceDocumentId,
                ExpectedCompanyContextDocumentId = companyContextDocumentId,
                ExpectedMatchingDocumentId = matchingDocumentId,
                ExpectedApplicationDocumentId = documentId,
                MaxMainContentCharacters = CoverLetterContentMetrics.DefaultMaxMainContentCharacters,
                EstimatedCharactersPerLine = CoverLetterContentMetrics.DefaultEstimatedCharactersPerLine
            };

            var verificationResult = await ExecuteMechanicalVerificationAsync(verificationRequest, cancellationToken);
            var wrappedDocument = BuildPhaseDocumentJson(
                job,
                definition,
                phaseState.AttemptCount,
                completedAtUtc,
                isRetry,
                JsonNode.Parse(outputJson.OutputJson),
                outputJson);

            var completionMessage = verificationResult.ApprovedForDownstream
                ? $"{definition.DisplayName}: completed through the in-process ApplyAI runtime."
                : $"{definition.DisplayName}: blocked by verification or downstream gate.";

            return new PhaseExecutionOutcome(
                wrappedDocument,
                verificationResult.VerificationJson,
                verificationResult.GateJson,
                verificationResult.WarningCount,
                verificationResult.ErrorCount,
                verificationResult.ApprovedForDownstream,
                completionMessage,
                completedAtUtc);
        }

        private PhaseExecutionOutcome ExecuteSyntheticPhase(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition definition,
            ApplyAiPipelinePhaseState phaseState,
            bool isRetry,
            DateTimeOffset startedAtUtc)
        {
            phaseState.CurrentActivity = PipelineActivity.ParsingResponse;
            job.CurrentActivity = PipelineActivity.ParsingResponse;
            phaseState.StatusMessage = $"{definition.DisplayName}: assembling synthetic JSON output.";
            job.StatusMessage = phaseState.StatusMessage;

            phaseState.CurrentActivity = PipelineActivity.RunningVerification;
            job.CurrentActivity = PipelineActivity.RunningVerification;
            phaseState.StatusMessage = $"{definition.DisplayName}: generating synthetic verification output.";
            job.StatusMessage = phaseState.StatusMessage;

            var completedAtUtc = startedAtUtc.AddMilliseconds(4);
            return new PhaseExecutionOutcome(
                BuildPhaseDocumentJson(job, definition, phaseState.AttemptCount, completedAtUtc, isRetry),
                BuildVerificationJson(definition, completedAtUtc, definition.VerificationSchemaPath is null ? "not_applicable" : "pass"),
                BuildGateJson(completedAtUtc, approvedForDownstream: true, hasPendingEdits: false),
                0,
                0,
                true,
                $"{definition.DisplayName}: completed by the Backend.api test implementation.",
                completedAtUtc);
        }

        private void ApplyRetryOverrides(ApplyAiPipelineJob job, PipelinePhase phase, ApplyAiPhaseRetryRequest? request)
        {
            if (request?.CompanyContextOverrides is not null && phase == PipelinePhase.CompanyContext)
            {
                job.CompanyNameOverride = request.CompanyContextOverrides.CompanyName;
                job.ApplicantAddressHint = request.CompanyContextOverrides.ApplicantAddressHint;
            }

            if (request is not null && phase == PipelinePhase.ApplicationGeneration && HasPreferences(request.PreferencesOverride))
            {
                job.PreferencesSnapshotJson = SerializeNullableElement(request.PreferencesOverride);
            }
        }

        private static void ResetPhaseStateForRetry(ApplyAiPipelinePhaseState phaseState)
        {
            phaseState.Status = PipelinePhaseStatus.Pending;
            phaseState.CurrentActivity = null;
            phaseState.StatusMessage = $"{PipelinePhaseCatalog.Get(phaseState.Phase).DisplayName}: retry pending.";
            phaseState.ApprovalRequired = false;
            phaseState.ApprovedForDownstream = false;
            phaseState.HasUnverifiedEdits = false;
            phaseState.CompletedAtUtc = null;
            phaseState.ApprovedAtUtc = null;
            phaseState.DocumentId = null;
            phaseState.DocumentJson = null;
            phaseState.VerificationJson = null;
            phaseState.GateJson = null;
            phaseState.WarningCount = 0;
            phaseState.ErrorCount = 0;
        }

        private void RemovePhaseArtifacts(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var existingArtifacts = job.Artifacts.Where(item => item.Phase == phase).ToArray();
            if (existingArtifacts.Length == 0)
            {
                return;
            }

            _db.ApplyAiPipelineArtifacts.RemoveRange(existingArtifacts);
            foreach (var artifact in existingArtifacts)
            {
                job.Artifacts.Remove(artifact);
            }
        }

        private void RebuildPhaseArtifacts(ApplyAiPipelineJob job, PipelinePhase phase, string? documentId)
        {
            var phaseState = GetPhaseState(job, phase);
            var resolvedDocumentId = string.IsNullOrWhiteSpace(documentId)
                ? BuildDocumentId(job, phase, Math.Max(phaseState.AttemptCount, 1))
                : documentId;

            RemovePhaseArtifacts(job, phase);
            foreach (var artifact in BuildArtifacts(job, phase, resolvedDocumentId))
            {
                AddArtifact(job, artifact);
            }
        }

        private async Task PersistPhaseArtifactsAsync(ApplyAiPipelineJob job, PipelinePhase phase, CancellationToken cancellationToken)
        {
            var runStoragePrefix = EnsureRunStoragePrefix(job);
            var artifacts = job.Artifacts
                .Where(item => item.Phase == phase)
                .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var artifact in artifacts)
            {
                var artifactContent = await BuildComputedArtifactContentAsync(job, artifact, cancellationToken);
                var storedArtifact = await _artifactStorage.StoreArtifactAsync(
                    artifact.Id,
                    runStoragePrefix,
                    artifact.RelativePath,
                    artifactContent.Content,
                    artifact.DisplayName,
                    artifactContent.MediaType,
                    cancellationToken);

                artifact.StorageKey = storedArtifact.StorageKey;
                artifact.RelativePath = storedArtifact.RelativePath;
                artifact.DisplayName = storedArtifact.DisplayName;
                artifact.MediaType = storedArtifact.MediaType;
            }
        }

        private void AddArtifact(ApplyAiPipelineJob job, ApplyAiPipelineArtifact artifact)
        {
            job.Artifacts.Add(artifact);

            var jobState = _db.Entry(job).State;
            if (jobState is not EntityState.Detached and not EntityState.Added)
            {
                _db.Entry(artifact).State = EntityState.Added;
            }
        }

        private ApplyAiPipelinePhaseState GetPhaseState(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var phaseState = job.PhaseStates.FirstOrDefault(item => item.Phase == phase);
            if (phaseState is null)
            {
                throw new KeyNotFoundException($"No stored phase state exists for phase '{phase}'.");
            }

            return phaseState;
        }

        private static void EnsureEditablePhase(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            if (job.WorkflowMode != PipelineWorkflowMode.Manual)
            {
                throw new InvalidOperationException("Phase editing and approval are only available in manual workflow mode.");
            }

            if (job.Status != PipelineJobStatus.AwaitingUserAction || job.CurrentPhase != phase)
            {
                throw new InvalidOperationException("The requested phase is not awaiting manual review.");
            }
        }

        private static void EnsurePhaseDocumentCanBeUpdated(
            ApplyAiPipelineJob job,
            ApplyAiPipelinePhaseState phaseState)
        {
            var isManualCurrentPhase = job.WorkflowMode == PipelineWorkflowMode.Manual
                && job.Status == PipelineJobStatus.AwaitingUserAction
                && job.CurrentPhase == phaseState.Phase;
            if (isManualCurrentPhase)
            {
                return;
            }

            var hasStoredDocument = !string.IsNullOrWhiteSpace(phaseState.DocumentId)
                && !string.IsNullOrWhiteSpace(phaseState.DocumentJson);
            if (hasStoredDocument && job.Status is PipelineJobStatus.Completed or PipelineJobStatus.Failed)
            {
                return;
            }

            throw new InvalidOperationException("The requested phase document is not available for editing in the current job state.");
        }

        private void EnsurePhaseCanBeRetried(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            if (job.CurrentPhase == phase && job.Status is PipelineJobStatus.AwaitingUserAction or PipelineJobStatus.Failed)
            {
                return;
            }

            if (job.Status is PipelineJobStatus.Completed or PipelineJobStatus.Failed && CanRetryPersistedPhase(job, phase))
            {
                return;
            }

            throw new InvalidOperationException("The current job state does not allow retry of the selected phase.");
        }

        private bool CanRetryPersistedPhase(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            return phase switch
            {
                PipelinePhase.CompanyContext => HasStoredJobPostingArtifact(job),
                PipelinePhase.Requirements => HasStoredJobPostingArtifact(job),
                PipelinePhase.CandidateEvidence => HasUsableDocument(job, PipelinePhase.Requirements),
                PipelinePhase.Matching => HasUsableDocument(job, PipelinePhase.Requirements)
                    && HasUsableDocument(job, PipelinePhase.CandidateEvidence),
                PipelinePhase.ApplicationGeneration => HasUsableDocument(job, PipelinePhase.Requirements)
                    && HasUsableDocument(job, PipelinePhase.CompanyContext)
                    && HasUsableDocument(job, PipelinePhase.CandidateEvidence)
                    && HasUsableDocument(job, PipelinePhase.Matching)
                    && !string.IsNullOrWhiteSpace(job.PreferencesSnapshotJson),
                _ => false,
            };
        }

        private void InvalidatePhaseAndDownstream(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var phaseIndex = PipelinePhaseCatalog.IndexOf(phase);
            foreach (var definition in PipelinePhaseCatalog.All.Where(definition => PipelinePhaseCatalog.IndexOf(definition.Phase) >= phaseIndex))
            {
                RemovePhaseArtifacts(job, definition.Phase);
                ResetPhaseStateForRetry(GetPhaseState(job, definition.Phase));
            }
        }

        private static int CalculateProgressPercent(ApplyAiPipelineJob job)
        {
            if (job.Status == PipelineJobStatus.Completed)
            {
                return 100;
            }

            var totalPhases = PipelinePhaseCatalog.All.Count;
            var completedPhases = job.PhaseStates.Count(item => item.Status is PipelinePhaseStatus.Completed or PipelinePhaseStatus.AwaitingApproval);
            return totalPhases == 0 ? 0 : completedPhases * 100 / totalPhases;
        }

        private async Task EnsureStoredJobPostingAsync(ApplyAiPipelineJob job, CancellationToken cancellationToken)
        {
            if (HasStoredJobPostingArtifact(job))
            {
                return;
            }

            if (job.JobPostingSourceType != PipelineInputKind.RemoteUrl)
            {
                throw new InvalidOperationException("No stored job posting artifact was found for the queued pipeline job.");
            }

            if (!Uri.TryCreate(job.JobPostingReference, UriKind.Absolute, out var linkUrl))
            {
                throw new InvalidOperationException("The stored job-posting URL is missing or invalid.");
            }

            var renderedPdf = await _jobPostingPdfRenderer.RenderAsync(
                linkUrl,
                cancellationToken,
                async (statusMessage, token) =>
                {
                    await UpdateQueuedJobProgressAsync(job, PipelineActivity.HydratingUserContext, statusMessage, token);
                });

            await UpdateQueuedJobProgressAsync(job, PipelineActivity.PersistingArtifacts, "Storing rendered job posting", cancellationToken);

            var runStoragePrefix = EnsureRunStoragePrefix(job);

            await using var stream = new MemoryStream(renderedPdf.Content, writable: false);
            var storedJobPosting = await _artifactStorage.StoreJobPostingAsync(
                job.Id,
                runStoragePrefix,
                stream,
                renderedPdf.FileName,
                renderedPdf.ContentType,
                cancellationToken);

            AddArtifact(job, CreateStoredJobPostingArtifact(job, storedJobPosting));
            job.JobPostingOriginalFileName = renderedPdf.FileName;
            job.JobPostingContentType = renderedPdf.ContentType;
            job.UpdatedAtUtc = DateTimeOffset.UtcNow;

            AppendEvent(
                job,
                PipelineEventType.JobProgressUpdated,
                null,
                PipelineActivity.PersistingArtifacts,
                "Rendered job posting stored.",
                job.UpdatedAtUtc);

            await _jobStore.SaveChangesAsync(cancellationToken);
        }

        private static bool HasStoredJobPostingArtifact(ApplyAiPipelineJob job)
        {
            return job.Artifacts.Any(item => item.Phase is null && item.IsPrimary && !string.IsNullOrWhiteSpace(item.StorageKey));
        }

        private async Task UpdateQueuedJobProgressAsync(
            ApplyAiPipelineJob job,
            PipelineActivity activity,
            string statusMessage,
            CancellationToken cancellationToken)
        {
            var updatedAtUtc = DateTimeOffset.UtcNow;
            job.Status = PipelineJobStatus.Running;
            job.CurrentPhase = null;
            job.CurrentActivity = activity;
            job.StatusMessage = statusMessage;
            job.UpdatedAtUtc = updatedAtUtc;

            AppendEvent(job, PipelineEventType.JobProgressUpdated, null, activity, statusMessage, updatedAtUtc);
            await _jobStore.SaveChangesAsync(cancellationToken);
        }

        private async Task UpdatePhaseProgressAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelinePhaseState phaseState,
            PipelineActivity activity,
            string statusMessage,
            CancellationToken cancellationToken)
        {
            var updatedAtUtc = DateTimeOffset.UtcNow;
            phaseState.Status = PipelinePhaseStatus.Running;
            phaseState.CurrentActivity = activity;
            phaseState.StatusMessage = statusMessage;

            job.Status = PipelineJobStatus.Running;
            job.CurrentPhase = phaseState.Phase;
            job.CurrentActivity = activity;
            job.StatusMessage = statusMessage;
            job.UpdatedAtUtc = updatedAtUtc;

            AppendEvent(job, PipelineEventType.JobProgressUpdated, phaseState.Phase, activity, statusMessage, updatedAtUtc);
            await _jobStore.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkQueuedJobFailedAsync(
            ApplyAiPipelineJob job,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            var failedAtUtc = DateTimeOffset.UtcNow;
            job.Status = PipelineJobStatus.Failed;
            job.CurrentActivity = PipelineActivity.Failed;
            job.StatusMessage = failureMessage;
            job.UpdatedAtUtc = failedAtUtc;
            job.CompletedAtUtc = failedAtUtc;
            job.ProgressPercent = CalculateProgressPercent(job);

            AppendEvent(job, PipelineEventType.JobFailed, job.CurrentPhase, PipelineActivity.Failed, failureMessage, failedAtUtc);
            await _jobStore.SaveChangesAsync(cancellationToken);
        }

        private static string BuildUserFacingPhaseStatusMessage(PipelinePhaseDefinition definition)
        {
            return definition.Phase switch
            {
                PipelinePhase.CompanyContext => "Performing Company Context Research",
                PipelinePhase.Requirements => "Preparing requirement analysis",
                _ => $"{definition.DisplayName}: running inside the Backend.api ApplyAI runtime.",
            };
        }

        private static void MarkJobCompleted(ApplyAiPipelineJob job, DateTimeOffset completedAtUtc, string statusMessage)
        {
            job.Status = PipelineJobStatus.Completed;
            job.CurrentPhase = null;
            job.CurrentActivity = PipelineActivity.Completed;
            job.StatusMessage = statusMessage;
            job.ProgressPercent = 100;
            job.UpdatedAtUtc = completedAtUtc;
            job.CompletedAtUtc = completedAtUtc;
        }

        private void AppendEvent(
            ApplyAiPipelineJob job,
            PipelineEventType eventType,
            PipelinePhase? phase,
            PipelineActivity activity,
            string message,
            DateTimeOffset occurredAtUtc)
        {
            var pipelineEvent = new ApplyAiPipelineEvent
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Job = job,
                EventId = Guid.NewGuid().ToString("N"),
                EventType = eventType,
                JobStatus = job.Status,
                Phase = phase,
                Activity = activity,
                ProgressPercent = job.ProgressPercent,
                Message = message,
                OccurredAtUtc = occurredAtUtc
            };

            job.Events.Add(pipelineEvent);

            var jobState = _db.Entry(job).State;
            if (jobState is not EntityState.Detached and not EntityState.Added)
            {
                _db.Entry(pipelineEvent).State = EntityState.Added;
            }
        }

        private static string BuildDocumentId(ApplyAiPipelineJob job, PipelinePhase phase, int attemptCount)
        {
            return $"{job.Id:N}:{PipelinePhaseCatalog.ToRouteSegment(phase)}:attempt-{attemptCount}";
        }

        private async Task<ApplyAiArtifactContentResponse> GetStoredJobPostingContentAsync(ApplyAiPipelineJob job, CancellationToken cancellationToken)
        {
            var artifact = job.Artifacts.FirstOrDefault(item => item.Phase is null && item.IsPrimary && !string.IsNullOrWhiteSpace(item.StorageKey));
            if (artifact is null)
            {
                throw new InvalidOperationException("No stored job posting artifact was found for the pipeline job.");
            }

            return await _artifactStorage.DownloadArtifactAsync(artifact.StorageKey!, artifact.DisplayName, artifact.MediaType, cancellationToken);
        }

        private async Task<ApplyAiStageVerificationResult> ExecuteMechanicalVerificationAsync(
            StageVerificationRequest verificationRequest,
            CancellationToken cancellationToken)
        {
            var verificationResult = await _verificationOrchestrator.VerifyStageAsync(verificationRequest, cancellationToken);
            var gateResult = _downstreamGateEvaluator.Evaluate(verificationRequest, verificationResult);

            var response = new StageVerificationResult
            {
                Stage = verificationResult.Stage,
                DocumentId = verificationResult.DocumentId,
                VerificationMode = "mechanical_with_gate",
                Status = verificationResult.Status,
                ApprovedForDownstream = verificationResult.ApprovedForDownstream && gateResult.ApprovedForDownstream,
                WarningCount = verificationResult.WarningCount,
                ErrorCount = verificationResult.ErrorCount,
                ArtifactPath = string.Empty,
                GateArtifactPath = string.Empty,
                Gate = gateResult,
                Findings = verificationResult.Findings
            };

            return new ApplyAiStageVerificationResult(
                JsonSerializer.Serialize(response, RuntimeJsonOptions),
                JsonSerializer.Serialize(gateResult, RuntimeJsonOptions),
                response.ApprovedForDownstream,
                response.WarningCount,
                response.ErrorCount,
                response.Status);
        }

        private async Task<MaterializedCandidateFiles> MaterializeCandidateFilesAsync(ApplyAiPipelineJob job, CancellationToken cancellationToken)
        {
            var selectedFileIds = DeserializeSelectedFileIds(job);
            if (selectedFileIds.Length == 0)
            {
                throw new InvalidOperationException("Candidate evidence cannot run because the pipeline job has no selected candidate files.");
            }

            var storedFiles = await _db.S3Files
                .AsNoTracking()
                .Where(file => file.UserId == job.UserId && selectedFileIds.Contains(file.Id))
                .ToListAsync(cancellationToken);

            var filesById = storedFiles.ToDictionary(file => file.Id);
            var orderedFiles = new List<S3File>(selectedFileIds.Length);
            foreach (var fileId in selectedFileIds)
            {
                if (!filesById.TryGetValue(fileId, out var storedFile))
                {
                    throw new InvalidOperationException($"Candidate evidence cannot run because file '{fileId}' was not found for user '{job.UserId}'.");
                }

                orderedFiles.Add(storedFile);
            }

            var duplicateFileNames = orderedFiles
                .GroupBy(file => Path.GetFileName(file.FileName), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateFileNames.Length > 0)
            {
                throw new InvalidOperationException($"Candidate evidence cannot run with duplicate file names in the selected candidate set: {string.Join(", ", duplicateFileNames)}");
            }

            var tempDirectory = CreateTemporaryWorkingDirectory("applyai-candidate-evidence-inputs");
            try
            {
                var filePaths = new List<string>(orderedFiles.Count);
                var fileNames = new List<string>(orderedFiles.Count);
                foreach (var storedFile in orderedFiles)
                {
                    var fileName = Path.GetFileName(storedFile.FileName);
                    var destinationPath = Path.Combine(tempDirectory, fileName);
                    var content = await _storageService.DownloadFileContentAsync(storedFile.S3Key, cancellationToken);
                    await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);

                    filePaths.Add(destinationPath);
                    fileNames.Add(fileName);
                }

                return new MaterializedCandidateFiles(tempDirectory, filePaths, fileNames);
            }
            catch
            {
                DeleteTemporaryWorkingDirectory(tempDirectory);
                throw;
            }
        }

        private async Task<string?> BuildApplicantProfileTextAsync(Guid userId, CancellationToken cancellationToken)
        {
            var profile = await _db.Profiles
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserId == userId, cancellationToken);

            if (profile is null)
            {
                return null;
            }

            var enhancement = ProfileDefaults.DeserializeProfileEnhancement(profile.ProfileEnhancementJson);
            var builder = new StringBuilder();
            builder.AppendLine($"Navn: {profile.FullName}");
            builder.AppendLine($"Ansøger-id: {profile.ApplicantId}");
            if (!string.IsNullOrWhiteSpace(profile.Municipality))
            {
                builder.AppendLine($"Lokation: {profile.Municipality}");
            }
            if (!string.IsNullOrWhiteSpace(profile.ShortBio))
            {
                builder.AppendLine($"Kort profil: {profile.ShortBio}");
            }
            if (!string.IsNullOrWhiteSpace(enhancement.Headline))
            {
                builder.AppendLine($"Headline: {enhancement.Headline}");
            }
            if (!string.IsNullOrWhiteSpace(enhancement.CurrentFocus))
            {
                builder.AppendLine($"Nuværende fokus: {enhancement.CurrentFocus}");
            }
            if (enhancement.PreferredRoles.Count > 0)
            {
                builder.AppendLine($"Foretrukne roller: {string.Join(", ", enhancement.PreferredRoles)}");
            }
            if (enhancement.CoreCompetencies.Count > 0)
            {
                builder.AppendLine($"Kernekompetencer: {string.Join(", ", enhancement.CoreCompetencies)}");
            }
            if (enhancement.KeyStrengths.Count > 0)
            {
                builder.AppendLine($"Styrker: {string.Join(", ", enhancement.KeyStrengths)}");
            }
            if (!string.IsNullOrWhiteSpace(enhancement.MobilityAndWorkPreferences))
            {
                builder.AppendLine($"Arbejdspræferencer: {enhancement.MobilityAndWorkPreferences}");
            }

            return builder.ToString().Trim();
        }

        private static List<string> BuildDisallowedJobPostingFileNames(ApplyAiPipelineJob job)
        {
            var disallowedFiles = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(job.JobPostingOriginalFileName))
            {
                disallowedFiles.Add(job.JobPostingOriginalFileName);
            }

            var storedArtifact = job.Artifacts.FirstOrDefault(item => item.Phase is null && item.IsPrimary);
            if (storedArtifact is not null && !string.IsNullOrWhiteSpace(storedArtifact.DisplayName))
            {
                disallowedFiles.Add(storedArtifact.DisplayName);
            }

            return disallowedFiles.ToList();
        }

        private static string CreateTemporaryWorkingDirectory(string prefix)
        {
            var tempDirectory = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            return tempDirectory;
        }

        private static void DeleteTemporaryWorkingDirectory(string tempDirectory)
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for transient runtime files.
            }
        }

        private static Guid[] DeserializeSelectedFileIds(ApplyAiPipelineJob job)
        {
            return JsonSerializer.Deserialize<Guid[]>(job.SelectedFileIdsJson) ?? [];
        }

        private static string ExtractPhaseOutputJson(ApplyAiPipelinePhaseState phaseState)
        {
            if (string.IsNullOrWhiteSpace(phaseState.DocumentJson))
            {
                throw new InvalidOperationException($"Phase '{phaseState.Phase}' does not have a stored document JSON payload.");
            }

            using var document = JsonDocument.Parse(phaseState.DocumentJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("phaseOutput", out var phaseOutput)
                && phaseOutput.ValueKind == JsonValueKind.Object)
            {
                return phaseOutput.GetRawText();
            }

            return phaseState.DocumentJson;
        }

        private static string GetRequiredDocumentId(ApplyAiPipelinePhaseState phaseState, PipelinePhase phase)
        {
            if (string.IsNullOrWhiteSpace(phaseState.DocumentId))
            {
                throw new InvalidOperationException($"Phase '{phase}' does not have a stored document id.");
            }

            return phaseState.DocumentId;
        }

        private string BuildPhaseDocumentJson(
            ApplyAiPipelineJob job,
            PipelinePhaseDefinition phaseDefinition,
            int attemptCount,
            DateTimeOffset generatedAtUtc,
            bool isRetry,
            JsonNode? phaseOutput = null,
            StructuredJsonGenerationResult? llmResult = null)
        {
            var candidateFiles = new JsonArray();
            foreach (var candidateFile in DeserializeCandidateFiles(job))
            {
                candidateFiles.Add(new JsonObject
                {
                    ["fileId"] = candidateFile.FileId.ToString(),
                    ["fileName"] = candidateFile.FileName,
                    ["uploadTimeUtc"] = candidateFile.UploadTimeUtc.ToString("O")
                });
            }

            var upstreamDocuments = new JsonArray();
            foreach (var upstreamPhase in job.PhaseStates.OrderBy(item => PipelinePhaseCatalog.IndexOf(item.Phase)))
            {
                if (!string.IsNullOrWhiteSpace(upstreamPhase.DocumentId))
                {
                    upstreamDocuments.Add(new JsonObject
                    {
                        ["phase"] = upstreamPhase.Phase.ToString(),
                        ["documentId"] = upstreamPhase.DocumentId
                    });
                }
            }

            var phaseJson = new JsonObject
            {
                ["testImplementation"] = true,
                ["jobId"] = job.Id.ToString("N"),
                ["phase"] = phaseDefinition.RouteSegment,
                ["phaseDisplayName"] = phaseDefinition.DisplayName,
                ["attempt"] = attemptCount,
                ["isRetry"] = isRetry,
                ["generatedAtUtc"] = generatedAtUtc.ToString("O"),
                ["workflowMode"] = job.WorkflowMode.ToString(),
                ["jobPostingSource"] = new JsonObject
                {
                    ["sourceType"] = job.JobPostingSourceType.ToString(),
                    ["reference"] = job.JobPostingReference,
                    ["fileName"] = job.JobPostingOriginalFileName,
                    ["contentType"] = job.JobPostingContentType
                },
                ["candidateFiles"] = candidateFiles,
                ["upstreamDocuments"] = upstreamDocuments,
                ["llmAssetPaths"] = new JsonObject
                {
                    ["promptPath"] = ApplyAiAssetPathResolver.ToLocalAssetReference(phaseDefinition.PromptPath),
                    ["outputSchemaPath"] = ApplyAiAssetPathResolver.ToLocalAssetReference(phaseDefinition.OutputSchemaPath),
                    ["verificationSchemaPath"] = ApplyAiAssetPathResolver.ToLocalAssetReference(phaseDefinition.VerificationSchemaPath)
                },
                ["companyContextOverrides"] = new JsonObject
                {
                    ["companyName"] = job.CompanyNameOverride,
                    ["applicantAddressHint"] = job.ApplicantAddressHint
                },
                ["preferencesProvided"] = !string.IsNullOrWhiteSpace(job.PreferencesSnapshotJson),
                ["phaseInputs"] = BuildPhaseInputsJson(job, phaseDefinition.Phase)
            };

            var storedJobPostingArtifact = job.Artifacts.FirstOrDefault(item => item.Phase is null && item.IsPrimary && !string.IsNullOrWhiteSpace(item.StorageKey));
            if (storedJobPostingArtifact is not null)
            {
                phaseJson["storedJobPostingArtifact"] = new JsonObject
                {
                    ["artifactId"] = storedJobPostingArtifact.Id.ToString("N"),
                    ["downloadUrl"] = $"/api/ai/pipeline/jobs/{job.Id:N}/artifacts/{storedJobPostingArtifact.Id:N}/content",
                    ["displayName"] = storedJobPostingArtifact.DisplayName,
                };
            }

            if (phaseDefinition.Phase == PipelinePhase.ApplicationGeneration && !string.IsNullOrWhiteSpace(job.PreferencesSnapshotJson))
            {
                phaseJson["preferencesSnapshot"] = JsonNode.Parse(job.PreferencesSnapshotJson);
            }

            if (llmResult is not null)
            {
                phaseJson["llmResponse"] = BuildLlmResponseJson(llmResult);
            }

            if (phaseOutput is not null)
            {
                phaseJson["phaseOutput"] = phaseOutput.DeepClone();
            }

            return phaseJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static JsonObject BuildLlmResponseJson(StructuredJsonGenerationResult llmResult)
        {
            return new JsonObject
            {
                ["model"] = llmResult.Model,
                ["responseId"] = llmResult.ResponseId,
                ["requestedDisplayCurrency"] = llmResult.RequestedDisplayCurrency,
                ["displayCurrency"] = llmResult.DisplayCurrency,
                ["tokenUsage"] = JsonSerializer.SerializeToNode(llmResult.TokenUsage, RuntimeJsonOptions),
                ["pricing"] = JsonSerializer.SerializeToNode(llmResult.Pricing, RuntimeJsonOptions),
                ["estimatedCost"] = JsonSerializer.SerializeToNode(llmResult.EstimatedCost, RuntimeJsonOptions),
                ["currencyExchange"] = JsonSerializer.SerializeToNode(llmResult.CurrencyExchange, RuntimeJsonOptions)
            };
        }

        private JsonObject BuildPhaseInputsJson(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var requestedArtifacts = DeserializeRequestedArtifacts(job);
            return phase switch
            {
                PipelinePhase.CompanyContext => new JsonObject
                {
                    ["jobPostingRequired"] = true,
                    ["candidateContextFileCount"] = DeserializeCandidateFiles(job).Length,
                    ["companyNameOverride"] = job.CompanyNameOverride,
                    ["applicantAddressHint"] = job.ApplicantAddressHint
                },
                PipelinePhase.Requirements => new JsonObject
                {
                    ["jobPostingRequired"] = true,
                    ["candidateFilesRequired"] = false
                },
                PipelinePhase.CandidateEvidence => new JsonObject
                {
                    ["requirementsDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.Requirements),
                    ["candidateFileCount"] = DeserializeCandidateFiles(job).Length
                },
                PipelinePhase.Matching => new JsonObject
                {
                    ["requirementsDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.Requirements),
                    ["candidateEvidenceDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.CandidateEvidence)
                },
                PipelinePhase.ApplicationGeneration => new JsonObject
                {
                    ["requirementsDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.Requirements),
                    ["candidateEvidenceDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.CandidateEvidence),
                    ["matchingDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.Matching),
                    ["companyContextDocumentAvailable"] = HasCompletedDocument(job, PipelinePhase.CompanyContext),
                    ["includeCoverLetterArtifacts"] = requestedArtifacts.IncludeCoverLetter,
                    ["includeFitAdvisory"] = requestedArtifacts.IncludeFitAdvisory
                },
                _ => new JsonObject()
            };
        }

        private static bool HasCompletedDocument(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var phaseState = job.PhaseStates.First(item => item.Phase == phase);
            return !string.IsNullOrWhiteSpace(phaseState.DocumentId)
                && phaseState.Status is PipelinePhaseStatus.Completed or PipelinePhaseStatus.AwaitingApproval;
        }

        private static bool HasUsableDocument(ApplyAiPipelineJob job, PipelinePhase phase)
        {
            var phaseState = job.PhaseStates.First(item => item.Phase == phase);
            return !string.IsNullOrWhiteSpace(phaseState.DocumentId)
                && !string.IsNullOrWhiteSpace(phaseState.DocumentJson);
        }

        private IEnumerable<ApplyAiPipelineArtifact> BuildArtifacts(ApplyAiPipelineJob job, PipelinePhase phase, string documentId)
        {
            var requestedArtifacts = DeserializeRequestedArtifacts(job);
            var routeSegment = PipelinePhaseCatalog.ToRouteSegment(phase);

            var artifacts = new List<ApplyAiPipelineArtifact>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.JsonDocument,
                    RelativePath = StoragePathBuilder.BuildPhaseDocumentRelativePath(phase),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildPhaseDocumentRelativePath(phase)),
                    MediaType = "application/json",
                    IsPrimary = true
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.VerificationReport,
                    RelativePath = StoragePathBuilder.BuildPhaseVerificationRelativePath(phase),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildPhaseVerificationRelativePath(phase)),
                    MediaType = "application/json"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.GateReport,
                    RelativePath = StoragePathBuilder.BuildPhaseGateRelativePath(phase),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildPhaseGateRelativePath(phase)),
                    MediaType = "application/json"
                }
            };

            if (phase == PipelinePhase.ApplicationGeneration && requestedArtifacts.IncludeCoverLetter)
            {
                artifacts.Add(new ApplyAiPipelineArtifact
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.HtmlDocument,
                    RelativePath = StoragePathBuilder.BuildCoverLetterHtmlRelativePath(),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildCoverLetterHtmlRelativePath()),
                    MediaType = "text/html"
                });
                artifacts.Add(new ApplyAiPipelineArtifact
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.CssStylesheet,
                    RelativePath = StoragePathBuilder.BuildCoverLetterCssRelativePath(),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildCoverLetterCssRelativePath()),
                    MediaType = "text/css"
                });
                artifacts.Add(new ApplyAiPipelineArtifact
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.PdfDocument,
                    RelativePath = StoragePathBuilder.BuildCoverLetterPdfRelativePath(),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildCoverLetterPdfRelativePath()),
                    MediaType = "application/pdf"
                });
            }

            if (phase == PipelinePhase.ApplicationGeneration && requestedArtifacts.IncludeFitAdvisory)
            {
                artifacts.Add(new ApplyAiPipelineArtifact
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    Job = job,
                    Phase = phase,
                    ArtifactKind = PipelineArtifactKind.Advisory,
                    RelativePath = StoragePathBuilder.BuildFitAdvisoryRelativePath(),
                    DisplayName = Path.GetFileName(StoragePathBuilder.BuildFitAdvisoryRelativePath()),
                    MediaType = "application/json"
                });
            }

            artifacts.Add(new ApplyAiPipelineArtifact
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                Job = job,
                Phase = phase,
                ArtifactKind = PipelineArtifactKind.Other,
                RelativePath = phase == PipelinePhase.ApplicationGeneration
                    ? StoragePathBuilder.BuildCoverLetterSummaryRelativePath()
                    : StoragePathBuilder.BuildPhaseMetadataRelativePath(phase),
                DisplayName = phase == PipelinePhase.ApplicationGeneration
                    ? Path.GetFileName(StoragePathBuilder.BuildCoverLetterSummaryRelativePath())
                    : Path.GetFileName(StoragePathBuilder.BuildPhaseMetadataRelativePath(phase)),
                MediaType = "application/json"
            });

            return artifacts;
        }

        private static ApplyAiRequestedArtifacts DeserializeRequestedArtifacts(ApplyAiPipelineJob job)
        {
            return JsonSerializer.Deserialize<ApplyAiRequestedArtifacts>(job.RequestedArtifactsJson) ?? new ApplyAiRequestedArtifacts();
        }

        private static ApplyAiCandidateFileSummary[] DeserializeCandidateFiles(ApplyAiPipelineJob job)
        {
            return JsonSerializer.Deserialize<ApplyAiCandidateFileSummary[]>(job.CandidateFileSnapshotJson) ?? [];
        }

        private static bool HasPreferences(JsonElement? preferencesOverride)
        {
            return preferencesOverride.HasValue && preferencesOverride.Value.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
        }

        private static string? SerializeNullableElement(JsonElement? element)
        {
            return HasPreferences(element) ? element!.Value.GetRawText() : null;
        }

        private static string BuildVerificationJson(
            PipelinePhaseDefinition phaseDefinition,
            DateTimeOffset verifiedAtUtc,
            string verificationStatus,
            string? editorComment = null)
        {
            var verificationJson = new JsonObject
            {
                ["testImplementation"] = true,
                ["status"] = verificationStatus,
                ["verifiedAtUtc"] = verifiedAtUtc.ToString("O"),
                ["verificationSchemaPath"] = ApplyAiAssetPathResolver.ToLocalAssetReference(phaseDefinition.VerificationSchemaPath),
                ["warnings"] = new JsonArray(),
                ["errors"] = new JsonArray()
            };

            if (!string.IsNullOrWhiteSpace(editorComment))
            {
                verificationJson["editorComment"] = editorComment;
            }

            return verificationJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static string BuildGateJson(DateTimeOffset checkedAtUtc, bool approvedForDownstream, bool hasPendingEdits)
        {
            var gateJson = new JsonObject
            {
                ["testImplementation"] = true,
                ["approvedForDownstream"] = approvedForDownstream,
                ["recommendedAction"] = approvedForDownstream ? "continue" : "review",
                ["hasPendingEdits"] = hasPendingEdits,
                ["checkedAtUtc"] = checkedAtUtc.ToString("O")
            };

            return gateJson.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private sealed record PhaseExecutionOutcome(
            string DocumentJson,
            string VerificationJson,
            string GateJson,
            int WarningCount,
            int ErrorCount,
            bool ApprovedForDownstream,
            string CompletionMessage,
            DateTimeOffset CompletedAtUtc);

        private sealed record MaterializedCandidateFiles(
            string TempDirectory,
            List<string> FilePaths,
            List<string> FileNames);

        private async Task<ApplyAiArtifactContentResponse> BuildComputedArtifactContentAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelineArtifact artifact,
            CancellationToken cancellationToken)
        {
            return artifact.ArtifactKind switch
            {
                PipelineArtifactKind.JsonDocument => BuildJsonArtifactContent(artifact.DisplayName, artifact.MediaType, GetRequiredPhaseState(job, artifact).DocumentJson),
                PipelineArtifactKind.VerificationReport => BuildJsonArtifactContent(artifact.DisplayName, artifact.MediaType, GetRequiredPhaseState(job, artifact).VerificationJson),
                PipelineArtifactKind.GateReport => BuildJsonArtifactContent(artifact.DisplayName, artifact.MediaType, GetRequiredPhaseState(job, artifact).GateJson),
                PipelineArtifactKind.HtmlDocument when artifact.Phase == PipelinePhase.ApplicationGeneration => await BuildCoverLetterHtmlArtifactAsync(job, artifact, cancellationToken),
                PipelineArtifactKind.CssStylesheet when artifact.Phase == PipelinePhase.ApplicationGeneration => await BuildCoverLetterCssArtifactAsync(job, artifact, cancellationToken),
                PipelineArtifactKind.PdfDocument when artifact.Phase == PipelinePhase.ApplicationGeneration => await BuildCoverLetterPdfArtifactAsync(job, artifact, cancellationToken),
                PipelineArtifactKind.Advisory when artifact.Phase == PipelinePhase.ApplicationGeneration => BuildJsonArtifactContent(artifact.DisplayName, artifact.MediaType, BuildFitAdvisoryArtifactJson(job)),
                PipelineArtifactKind.Other when artifact.Phase == PipelinePhase.ApplicationGeneration => BuildJsonArtifactContent(artifact.DisplayName, artifact.MediaType, await BuildApplicationArtifactMetadataJsonAsync(job, artifact, cancellationToken)),
                PipelineArtifactKind.HtmlDocument => new ApplyAiArtifactContentResponse(
                    Encoding.UTF8.GetBytes(BuildSyntheticHtmlArtifact(job, artifact)),
                    artifact.MediaType,
                    artifact.DisplayName),
                PipelineArtifactKind.CssStylesheet => new ApplyAiArtifactContentResponse(
                    Encoding.UTF8.GetBytes(BuildSyntheticCssArtifact()),
                    artifact.MediaType,
                    artifact.DisplayName),
                PipelineArtifactKind.Advisory or PipelineArtifactKind.Other => new ApplyAiArtifactContentResponse(
                    Encoding.UTF8.GetBytes(BuildSyntheticMetadataArtifact(job, artifact)),
                    artifact.MediaType,
                    artifact.DisplayName),
                _ => throw new KeyNotFoundException("Artifact content is not available in the current test implementation for the requested artifact type."),
            };
        }

        private async Task<ApplyAiArtifactContentResponse> BuildCoverLetterHtmlArtifactAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelineArtifact artifact,
            CancellationToken cancellationToken)
        {
            var renderResult = await RenderCoverLetterTemplateAsync(job, cancellationToken);
            return new ApplyAiArtifactContentResponse(
                Encoding.UTF8.GetBytes(renderResult.HtmlDocument),
                artifact.MediaType,
                artifact.DisplayName);
        }

        private async Task<ApplyAiArtifactContentResponse> BuildCoverLetterCssArtifactAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelineArtifact artifact,
            CancellationToken cancellationToken)
        {
            var renderResult = await RenderCoverLetterTemplateAsync(job, cancellationToken);
            return new ApplyAiArtifactContentResponse(
                Encoding.UTF8.GetBytes(renderResult.StylesheetText),
                artifact.MediaType,
                artifact.DisplayName);
        }

        private async Task<ApplyAiArtifactContentResponse> BuildCoverLetterPdfArtifactAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelineArtifact artifact,
            CancellationToken cancellationToken)
        {
            var applicationJson = ExtractPhaseOutputJson(GetPhaseState(job, PipelinePhase.ApplicationGeneration));
            var renderResult = await _coverLetterPdfRenderer.RenderAsync(applicationJson, cancellationToken);
            return new ApplyAiArtifactContentResponse(
                renderResult.PdfDocument,
                artifact.MediaType,
                artifact.DisplayName);
        }

        private async Task<CoverLetterTemplateRenderResult> RenderCoverLetterTemplateAsync(
            ApplyAiPipelineJob job,
            CancellationToken cancellationToken)
        {
            var applicationJson = ExtractPhaseOutputJson(GetPhaseState(job, PipelinePhase.ApplicationGeneration));
            return await _coverLetterTemplateRenderer.RenderAsync(applicationJson, cancellationToken);
        }

        private async Task<string> BuildApplicationArtifactMetadataJsonAsync(
            ApplyAiPipelineJob job,
            ApplyAiPipelineArtifact artifact,
            CancellationToken cancellationToken)
        {
            var renderResult = await RenderCoverLetterTemplateAsync(job, cancellationToken);
            var json = new JsonObject
            {
                ["testImplementation"] = false,
                ["jobId"] = job.Id.ToString("N"),
                ["artifactId"] = artifact.Id.ToString("N"),
                ["artifactKind"] = artifact.ArtifactKind.ToString(),
                ["displayName"] = artifact.DisplayName,
                ["phase"] = PipelinePhase.ApplicationGeneration.ToString(),
                ["coverLetter"] = new JsonObject
                {
                    ["mainContentCharacterCount"] = renderResult.MainContentCharacterCount,
                    ["mainContentBudgetUsage"] = renderResult.MainContentBudgetUsage,
                    ["maxMainContentCharacters"] = renderResult.MaxMainContentCharacters,
                    ["explicitLineBreakCount"] = renderResult.ExplicitLineBreakCount,
                    ["paragraphBreakCount"] = renderResult.ParagraphBreakCount,
                    ["estimatedCharactersPerLine"] = renderResult.EstimatedCharactersPerLine,
                    ["withinMainContentLimit"] = renderResult.WithinMainContentLimit,
                    ["missingFields"] = new JsonArray(renderResult.MissingFields.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                    ["warnings"] = new JsonArray(renderResult.Warnings.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray())
                }
            };

            return json.ToJsonString(ArtifactJsonOptions);
        }

        private string BuildFitAdvisoryArtifactJson(ApplyAiPipelineJob job)
        {
            var matchingPhaseState = GetPhaseState(job, PipelinePhase.Matching);
            var applicationPhaseState = GetPhaseState(job, PipelinePhase.ApplicationGeneration);
            var matchingJson = ExtractPhaseOutputJson(matchingPhaseState);

            using var matchingDocument = JsonDocument.Parse(matchingJson);
            var overallAssessment = matchingDocument.RootElement.TryGetProperty("overall_assessment", out var overallAssessmentElement)
                && overallAssessmentElement.ValueKind == JsonValueKind.Object
                ? overallAssessmentElement
                : default;

            var overallMatchLevel = overallAssessment.ValueKind == JsonValueKind.Object
                ? GetString(overallAssessment, "overall_match_level")
                : string.Empty;
            var majorGapRequirementIds = overallAssessment.ValueKind == JsonValueKind.Object
                ? GetStringArray(overallAssessment, "major_gap_requirement_ids")
                : [];
            var majorStrengthEvidenceIds = overallAssessment.ValueKind == JsonValueKind.Object
                ? GetStringArray(overallAssessment, "major_strength_evidence_ids")
                : [];

            var fitStrategy = TryExtractPreferencesSection(job.PreferencesSnapshotJson, "fit_strategy");
            var recommendation = applicationPhaseState.ApprovedForDownstream ? "continue" : "review";
            var summaryDa = applicationPhaseState.ApprovedForDownstream
                ? majorGapRequirementIds.Count > 0
                    ? $"Ansøgningen er genereret, men matching fremhæver {majorGapRequirementIds.Count} større gap(s), som bør gennemgås før afsendelse."
                    : "Ansøgningen er genereret og gated til downstream uden blokkerende issues."
                : "Ansøgningen blev genereret, men den endelige application-generation-gate kræver review før videre brug.";

            var json = new JsonObject
            {
                ["testImplementation"] = false,
                ["jobId"] = job.Id.ToString("N"),
                ["phase"] = "application_generation",
                ["generatedAtUtc"] = applicationPhaseState.CompletedAtUtc?.ToString("O") ?? DateTimeOffset.UtcNow.ToString("O"),
                ["overallMatchLevel"] = overallMatchLevel,
                ["majorGapRequirementIds"] = new JsonArray(majorGapRequirementIds.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["majorStrengthEvidenceIds"] = new JsonArray(majorStrengthEvidenceIds.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["matchingApprovedForDownstream"] = matchingPhaseState.ApprovedForDownstream,
                ["applicationApprovedForDownstream"] = applicationPhaseState.ApprovedForDownstream,
                ["matchingGate"] = ParseJsonNode(matchingPhaseState.GateJson),
                ["applicationGate"] = ParseJsonNode(applicationPhaseState.GateJson),
                ["recommendation"] = recommendation,
                ["summaryDa"] = summaryDa,
                ["fitStrategy"] = fitStrategy
            };

            return json.ToJsonString(ArtifactJsonOptions);
        }

        private static JsonNode? TryExtractPreferencesSection(string? preferencesJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(preferencesJson))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(preferencesJson) is JsonObject root && root[propertyName] is JsonNode node
                    ? node.DeepClone()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static JsonNode? ParseJsonNode(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonNode.Parse(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string GetString(JsonElement parent, string propertyName)
        {
            return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static List<string> GetStringArray(JsonElement parent, string propertyName)
        {
            if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return array.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
        }

        private ApplyAiPipelinePhaseState GetRequiredPhaseState(ApplyAiPipelineJob job, ApplyAiPipelineArtifact artifact)
        {
            if (!artifact.Phase.HasValue)
            {
                throw new KeyNotFoundException("The requested artifact is not linked to a pipeline phase document.");
            }

            return GetPhaseState(job, artifact.Phase.Value);
        }

        private static ApplyAiArtifactContentResponse BuildJsonArtifactContent(string fileName, string mediaType, string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new KeyNotFoundException("No persisted JSON content was found for the requested artifact.");
            }

            return new ApplyAiArtifactContentResponse(
                Encoding.UTF8.GetBytes(json),
                string.IsNullOrWhiteSpace(mediaType) ? "application/json" : mediaType,
                fileName);
        }

        private static string BuildSyntheticHtmlArtifact(ApplyAiPipelineJob job, ApplyAiPipelineArtifact artifact)
        {
            var phaseName = artifact.Phase.HasValue ? PipelinePhaseCatalog.Get(artifact.Phase.Value).DisplayName : "Pipeline artifact";
            return $"""
<!doctype html>
<html lang=\"en\">
  <head>
    <meta charset=\"utf-8\" />
    <title>{artifact.DisplayName}</title>
        <link rel=\"stylesheet\" href=\"cover_letter.css\" />
  </head>
  <body>
    <main>
      <h1>{phaseName}</h1>
      <p>This HTML artifact is a placeholder emitted by the current Backend.api test implementation for job {job.Id:N}.</p>
      <p>Use the phase document route for the canonical JSON output during the first demo flow.</p>
    </main>
  </body>
</html>
""";
        }

        private static string BuildSyntheticCssArtifact()
        {
            return "body { font-family: Arial, sans-serif; color: #111; margin: 2rem; } main { max-width: 48rem; } h1 { margin-bottom: 1rem; }";
        }

        private static string BuildSyntheticMetadataArtifact(ApplyAiPipelineJob job, ApplyAiPipelineArtifact artifact)
        {
            var json = new JsonObject
            {
                ["testImplementation"] = true,
                ["jobId"] = job.Id.ToString("N"),
                ["artifactId"] = artifact.Id.ToString("N"),
                ["artifactKind"] = artifact.ArtifactKind.ToString(),
                ["displayName"] = artifact.DisplayName,
                ["message"] = "This artifact is represented in the current test implementation but is not backed by a stored external file.",
            };

            return json.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private static string EnsureRunStoragePrefix(ApplyAiPipelineJob job)
        {
            var canonicalPrefix = StoragePathBuilder.BuildRunStoragePrefix(job.UserId, job.CreatedAtUtc, job.Id);
            if (!string.Equals(job.RunStoragePrefix, canonicalPrefix, StringComparison.Ordinal))
            {
                job.RunStoragePrefix = canonicalPrefix;
            }

            return canonicalPrefix;
        }

        private static string BuildRunStoragePrefix(Guid userId, DateTimeOffset createdAtUtc, Guid jobId)
        {
            return StoragePathBuilder.BuildRunStoragePrefix(userId, createdAtUtc, jobId);
        }

        private static ApplyAiRequestedArtifacts ParseRequestedArtifacts(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ApplyAiRequestedArtifacts();
            }

            return JsonSerializer.Deserialize<ApplyAiRequestedArtifacts>(json) ?? new ApplyAiRequestedArtifacts();
        }

        private static JsonElement? ParseNullableJsonElement(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }
}