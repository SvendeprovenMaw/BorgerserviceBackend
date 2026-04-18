using System.Text.Json;
using ApplyAI.LlmPipeline;
using Backend.api.Services.ApplyAIService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers
{
    /// <summary>
    /// Production-shaped ApplyAI pipeline routes hosted inside the backend API.
    /// </summary>
    /// <remarks>
    /// These routes are authenticated and resolve user context from the JWT access-token cookie.
    ///
    /// The current implementation already persists jobs, phases, artifacts, and events in the database,
    /// while some intake behavior is still transitional, such as durable upload processing and link-to-PDF conversion.
    /// </remarks>
    [ApiController]
    [Authorize]
    [Route("api/ai/pipeline")]
    public class AiController : ControllerBase
    {
        private readonly IApplyAIService _applyAiService;

        public AiController(IApplyAIService applyAiService)
        {
            _applyAiService = applyAiService;
        }

        /// <summary>
        /// Creates a pipeline job from a JSON request, usually for remote URL intake.
        /// </summary>
        /// <remarks>
        /// Use this route when the caller already has a job-posting URL or another non-file source descriptor.
        ///
        /// Input fields:
        /// - **workflowMode**: chooses whether the pipeline should continue automatically or stop for manual review between phases.
        /// - **jobPostingSource**: describes where the job posting comes from. On this route it is normally a remote URL.
        /// - **candidateDocuments**: selects which consented user-side files should be loaded from the database and Backblaze for the run.
        /// - **companyContextOverrides**: optional hints that help the company-context phase, for example an explicit company name or applicant address hint.
        /// - **preferencesOverride**: raw JSON preferences used by the application-generation phase. This is still required because preferences are not yet persisted in production storage.
        /// - **requestedArtifacts**: selects which output artifacts the current ApplyAI implementation should persist and expose back to the caller.
        /// - **correlationId**: optional caller-generated id used to correlate frontend actions with the created pipeline job.
        ///
        /// Example request:
        /// ```json
        /// {
        ///   "workflowMode": "Auto",
        ///   "jobPostingSource": {
        ///     "sourceType": "RemoteUrl",
        ///     "url": "https://example.com/job-posting.pdf"
        ///   },
        ///   "candidateDocuments": {
        ///     "includeCurrentCv": true,
        ///     "includeProfileRelevantDocuments": true,
        ///     "additionalFileIds": [],
        ///     "includeAllConsentedFiles": true
        ///   },
        ///   "companyContextOverrides": {
        ///     "companyName": "Acme Corp",
        ///     "applicantAddressHint": null
        ///   },
        ///   "preferencesOverride": {
        ///     "applicant_display_name": "Demo Applicant",
        ///     "applicant_id": "current-user",
        ///     "target_language": "da",
        ///     "tone": "warm_professional"
        ///   },
        ///   "requestedArtifacts": {
        ///     "includeFitAdvisory": true,
        ///     "includeCoverLetter": true
        ///   },
        ///   "correlationId": "frontend-demo-json-route"
        /// }
        /// ```
        ///
        /// Example conflict response:
        /// ```json
        /// {
        ///   "message": "missing_preferences: preferencesOverride is required for the test implementation because it always runs application_generation."
        /// }
        /// ```
        ///
        /// Other possible conflict payloads include `missing_file_consent`, `missing_active_terms_consent`, and `missing_candidate_documents`.
        /// </remarks>
        /// <param name="request">JSON job-creation payload that defines intake source, candidate-document selection, generation preferences, and requested artifacts.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>A created response that points at the persisted pipeline job.</returns>
        /// <response code="201">The pipeline job was created and the response includes the status and events URLs.</response>
        /// <response code="400">The request shape is invalid, the phase name is invalid, or the source reference could not be parsed.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="409">The authenticated user is missing required consent, candidate documents, or preferences for the current pipeline rules.</response>
        /// <response code="500">An unexpected backend failure occurred while creating the job.</response>
        [HttpPost("jobs")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(PipelineJobAcceptedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateJob([FromBody] ApplyAiJobRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var acceptedResponse = await _applyAiService.SubmitJobAsync(User, request, cancellationToken);
                return CreatedAtAction(nameof(GetJob), new { jobId = acceptedResponse.JobId }, acceptedResponse);
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Creates a pipeline job from a multipart file upload.
        /// </summary>
        /// <remarks>
        /// Use this route when the job posting is supplied as an uploaded file instead of a remote URL.
        ///
        /// Input fields:
        /// - **jobPostingFile**: the uploaded job-posting file. In the current frontend flow this is intended to be a PDF.
        /// - **workflowMode**: chooses whether the job auto-runs or pauses for manual approval.
        /// - **includeCurrentCv**: includes the current CV referenced from the authenticated user's profile.
        /// - **includeProfileRelevantDocuments**: includes relevant profile documents already attached to the user's profile.
        /// - **additionalFileIds**: explicit extra file ids to include when the caller wants more evidence than the profile defaults.
        /// - **includeAllConsentedFiles**: expands the candidate-document set to all consented user-owned files.
        /// - **companyName** and **applicantAddressHint**: optional enrichment hints for the company-context phase.
        /// - **preferencesOverrideJson**: serialized preferences JSON used by the application-generation phase.
        /// - **requestedArtifactsJson**: serialized artifact-selection object that tells the pipeline which outputs should be exposed.
        /// - **correlationId**: optional caller-generated trace id.
        ///
        /// Example multipart field set:
        /// - **jobPostingFile**: `job-posting.pdf`
        /// - **workflowMode**: `Auto`
        /// - **includeCurrentCv**: `true`
        /// - **includeProfileRelevantDocuments**: `true`
        /// - **additionalFileIds[]**: `11111111-1111-1111-1111-111111111111`
        /// - **includeAllConsentedFiles**: `true`
        /// - **companyName**: `Acme Corp`
        /// - **preferencesOverrideJson**: `{"applicant_display_name":"Demo Applicant","applicant_id":"current-user","target_language":"da"}`
        /// - **requestedArtifactsJson**: `{"includeFitAdvisory":true,"includeCoverLetter":true}`
        ///
        /// Example conflict response:
        /// ```json
        /// {
        ///   "message": "missing_candidate_documents: No consented candidate documents were available for the pipeline job."
        /// }
        /// ```
        ///
        /// Note:
        /// The current ApplyAI service persists the uploaded PDF under the pipeline run before the job is created, so the first demo flow now has a real stored input artifact instead of only an `upload://...` marker.
        /// </remarks>
        /// <param name="request">Multipart form payload containing the uploaded job posting and the pipeline options that should be used for the created run.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>A created response that points at the persisted pipeline job.</returns>
        /// <response code="201">The job posting was stored and the pipeline job was created.</response>
        /// <response code="400">The upload is missing, empty, or has an invalid request payload.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="409">The authenticated user is missing required consent, candidate documents, or preferences for the current pipeline rules.</response>
        /// <response code="500">An unexpected backend failure occurred while storing the upload or creating the job.</response>
        [HttpPost("jobs/upload")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(PipelineJobAcceptedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateJobFromUpload([FromForm] ApplyAiJobUploadRequest request, CancellationToken cancellationToken)
        {
            try
            {
                if (request.JobPostingFile is null || request.JobPostingFile.Length == 0)
                {
                    return BadRequest(new { message = "JobPostingFile is required." });
                }

                var acceptedResponse = await _applyAiService.SubmitUploadedJobAsync(User, request, cancellationToken);
                return CreatedAtAction(nameof(GetJob), new { jobId = acceptedResponse.JobId }, acceptedResponse);
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Creates a pipeline job from a job-posting page URL that must be rendered to PDF through the backend Playwright service.
        /// </summary>
        /// <remarks>
        /// Example request:
        /// ```json
        /// {
        ///   "url": "https://example.com/jobs/frontend-developer",
        ///   "workflowMode": "Auto",
        ///   "includeCurrentCv": true,
        ///   "includeProfileRelevantDocuments": true,
        ///   "includeAllConsentedFiles": true,
        ///   "companyName": "Acme Corp",
        ///   "preferencesOverride": {
        ///     "applicant_display_name": "Demo Applicant",
        ///     "applicant_id": "current-user",
        ///     "target_language": "da"
        ///   },
        ///   "requestedArtifacts": {
        ///     "includeFitAdvisory": true,
        ///     "includeCoverLetter": true
        ///   }
        /// }
        /// ```
        ///
        /// Example conflict response:
        /// ```json
        /// {
        ///   "message": "missing_file_consent: One or more selected files do not have active consent. Files: cv.pdf"
        /// }
        /// ```
        ///
        /// The backend loads the page with Playwright, renders a PDF, stores that PDF under the pipeline run, and then creates the pipeline job against the stored artifact.
        /// </remarks>
        /// <response code="201">The URL was rendered to PDF, stored, and used to create the pipeline job.</response>
        /// <response code="400">The request URL is missing, malformed, or not an absolute HTTP/HTTPS URL.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="409">The authenticated user is missing required consent, candidate documents, or preferences for the current pipeline rules.</response>
        /// <response code="500">An unexpected backend failure occurred while rendering the URL or creating the job.</response>
        [HttpPost("jobs/link")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(PipelineJobAcceptedResponse), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateJobFromLink([FromBody] ApplyAiJobLinkRequest request, CancellationToken cancellationToken)
        {
            try
            {
                var acceptedResponse = await _applyAiService.SubmitLinkedJobAsync(User, request, cancellationToken);
                return CreatedAtAction(nameof(GetJob), new { jobId = acceptedResponse.JobId }, acceptedResponse);
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Returns the current snapshot for one pipeline job.
        /// </summary>
        /// <remarks>
        /// Use this route for polling or page refresh recovery.
        ///
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id returned by the create-job route.
        ///
        /// The returned snapshot includes the overall status, current phase, current activity, progress, available actions, artifacts, and per-phase summaries.
        /// </remarks>
        /// <param name="jobId">Pipeline-job id to load.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The current persisted job snapshot.</returns>
        /// <response code="200">The job snapshot was found and returned.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        [HttpGet("jobs/{jobId}")]
        [ProducesResponseType(typeof(PipelineJobSnapshot), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJob(string jobId, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _applyAiService.GetJobAsync(User, jobId, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Returns the persisted event history for one pipeline job.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id returned by the create-job route.
        ///
        /// Ordinary HTTP callers receive the stored event list as JSON so they can poll or inspect run history.
        /// Callers that request `text/event-stream` receive the same persisted event history as a live SSE stream.
        /// </remarks>
        /// <param name="jobId">Pipeline-job id whose event history should be returned.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The persisted event sequence for the selected job, either as JSON or SSE.</returns>
        /// <response code="200">The stored event list was returned as JSON or streamed as SSE.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        [HttpGet("jobs/{jobId}/events")]
        [ProducesResponseType(typeof(IReadOnlyList<PipelineEventEnvelope>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetEvents(string jobId, CancellationToken cancellationToken)
        {
            try
            {
                if (RequestWantsServerSentEvents())
                {
                    await StreamEventsAsync(jobId, cancellationToken);
                    return new EmptyResult();
                }

                return Ok(await _applyAiService.GetEventsAsync(User, jobId, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new EmptyResult();
            }
            catch (Exception exception)
            {
                if (Response.HasStarted)
                {
                    throw;
                }

                return MapException(exception);
            }
        }

        /// <summary>
        /// Returns the artifact inventory for one pipeline job.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id returned by the create-job route.
        ///
        /// The artifact list shows which generated or stored outputs belong to the run, including their phase, type, display name, and logical download route.
        /// </remarks>
        /// <param name="jobId">Pipeline-job id whose artifact inventory should be returned.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The artifact inventory for the selected job.</returns>
        /// <response code="200">The artifact inventory was returned.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        [HttpGet("jobs/{jobId}/artifacts")]
        [ProducesResponseType(typeof(IReadOnlyList<PipelineArtifactReference>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetArtifacts(string jobId, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _applyAiService.GetArtifactsAsync(User, jobId, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Streams one artifact through a backend-controlled download route.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **jobId**: the pipeline job that owns the artifact.
        /// - **artifactId**: the artifact identifier returned from the artifact inventory.
        ///
        /// Stored job-posting PDFs are downloaded from backend-controlled storage. Synthetic JSON artifacts are generated from the persisted phase state when possible.
        /// </remarks>
        /// <response code="200">The artifact content was found and streamed through the backend.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job or artifact was not found for the current user, or the current test implementation cannot generate the requested artifact content.</response>
        [HttpGet("jobs/{jobId}/artifacts/{artifactId}/content")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetArtifactContent(string jobId, string artifactId, CancellationToken cancellationToken)
        {
            try
            {
                var artifactContent = await _applyAiService.GetArtifactContentAsync(User, jobId, artifactId, cancellationToken);
                return File(artifactContent.Content, artifactContent.MediaType, artifactContent.FileName);
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Returns the current persisted document for a single pipeline phase.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id.
        /// - **phase**: the phase route segment to inspect, for example `requirements`, `company_context`, `candidate_evidence`, `matching`, or `application_generation`.
        ///
        /// The response contains the saved phase document, verification result, gate result, editability flags, and related artifacts.
        /// </remarks>
        /// <param name="jobId">Pipeline-job id that owns the phase document.</param>
        /// <param name="phase">Route segment or enum name for the pipeline phase to inspect.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The current phase document and its verification metadata.</returns>
        [HttpGet("jobs/{jobId}/phases/{phase}")]
        public async Task<IActionResult> GetPhaseDocument(string jobId, string phase, CancellationToken cancellationToken)
        {
            if (!TryResolvePhase(phase, out var resolvedPhase))
            {
                return BadRequest(new { message = $"Unsupported phase '{phase}'." });
            }

            try
            {
                return Ok(await _applyAiService.GetPhaseDocumentAsync(User, jobId, resolvedPhase, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Replaces the current phase document with edited JSON.
        /// </summary>
        /// <remarks>
        /// Use this route during manual review when a reviewer wants to correct the current phase output before approval.
        ///
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id.
        /// - **phase**: the phase whose current document should be replaced.
        /// - **documentJson**: the edited JSON payload that should become the new canonical phase document.
        /// - **editorComment**: optional operator note explaining why the phase document was changed.
        ///
        /// Example invalid-transition response:
        /// ```json
        /// {
        ///   "message": "The requested phase is not awaiting manual review."
        /// }
        /// ```
        /// </remarks>
        /// <param name="jobId">Pipeline-job id that owns the phase document.</param>
        /// <param name="phase">Route segment or enum name for the pipeline phase to update.</param>
        /// <param name="request">Edited JSON document plus an optional reviewer comment.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The updated job snapshot after the edited phase document has been persisted and re-evaluated.</returns>
        /// <response code="200">The phase document was updated and re-evaluated.</response>
        /// <response code="400">The phase route segment or the request body is invalid.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        /// <response code="409">The pipeline is not in a state that allows editing the requested phase.</response>
        [HttpPut("jobs/{jobId}/phases/{phase}/document")]
        [ProducesResponseType(typeof(ApplyAiPhaseDocumentResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> UpdatePhaseDocument(string jobId, string phase, [FromBody] ApplyAiPhaseDocumentUpdateRequest request, CancellationToken cancellationToken)
        {
            if (!TryResolvePhase(phase, out var resolvedPhase))
            {
                return BadRequest(new { message = $"Unsupported phase '{phase}'." });
            }

            try
            {
                return Ok(await _applyAiService.UpdatePhaseDocumentAsync(User, jobId, resolvedPhase, request, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Approves the current phase output and allows the pipeline to continue.
        /// </summary>
        /// <remarks>
        /// Use this route when a reviewer accepts the current phase document and wants the pipeline to advance.
        ///
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id.
        /// - **phase**: the phase being approved.
        /// - **comment**: optional reviewer note stored alongside the approval action.
        ///
        /// Example conflict response:
        /// ```json
        /// {
        ///   "message": "Phase editing and approval are only available in manual workflow mode."
        /// }
        /// ```
        /// </remarks>
        /// <param name="jobId">Pipeline-job id that owns the phase.</param>
        /// <param name="phase">Route segment or enum name for the phase to approve.</param>
        /// <param name="request">Optional approval note from the reviewer.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The updated job snapshot after the approval has been recorded.</returns>
        /// <response code="200">The phase was approved and the job was advanced or completed.</response>
        /// <response code="400">The phase route segment is invalid.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        /// <response code="409">The pipeline is not in a state that allows approval of the requested phase.</response>
        [HttpPost("jobs/{jobId}/phases/{phase}/approve")]
        [ProducesResponseType(typeof(PipelineJobSnapshot), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ApprovePhase(string jobId, string phase, [FromBody] ApplyAiPhaseApprovalRequest? request, CancellationToken cancellationToken)
        {
            if (!TryResolvePhase(phase, out var resolvedPhase))
            {
                return BadRequest(new { message = $"Unsupported phase '{phase}'." });
            }

            try
            {
                return Ok(await _applyAiService.ApprovePhaseAsync(User, jobId, resolvedPhase, request, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        /// <summary>
        /// Re-runs one pipeline phase.
        /// </summary>
        /// <remarks>
        /// Use this route when a phase failed or when a reviewer wants a fresh output after changing upstream context.
        ///
        /// Input fields:
        /// - **jobId**: the persisted pipeline-job id.
        /// - **phase**: the phase that should be re-run.
        /// - **reason**: optional operator note that explains why a retry was requested.
        /// - **companyContextOverrides**: optional replacement hints used only when they are relevant to the selected phase.
        /// - **preferencesOverride**: optional replacement preferences JSON, mainly relevant when retrying `application_generation`.
        ///
        /// Example request:
        /// ```json
        /// {
        ///   "reason": "Retry after updating the company name.",
        ///   "companyContextOverrides": {
        ///     "companyName": "Updated Company",
        ///     "applicantAddressHint": null
        ///   },
        ///   "preferencesOverride": null
        /// }
        /// ```
        /// </remarks>
        /// <param name="jobId">Pipeline-job id that owns the phase.</param>
        /// <param name="phase">Route segment or enum name for the phase to retry.</param>
        /// <param name="request">Retry reason and any phase-relevant override payloads.</param>
        /// <param name="cancellationToken">Cancellation token for the HTTP request.</param>
        /// <returns>The updated job snapshot after the retry request has been persisted.</returns>
        /// <response code="200">The phase retry was accepted and the job snapshot was updated.</response>
        /// <response code="400">The phase route segment is invalid.</response>
        /// <response code="401">The caller is not authenticated with a valid AccessToken cookie.</response>
        /// <response code="404">The job id does not exist for the current user.</response>
        /// <response code="409">The pipeline is not in a state that allows retry of the selected phase.</response>
        [HttpPost("jobs/{jobId}/phases/{phase}/retry")]
        [ProducesResponseType(typeof(PipelineJobSnapshot), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> RetryPhase(string jobId, string phase, [FromBody] ApplyAiPhaseRetryRequest? request, CancellationToken cancellationToken)
        {
            if (!TryResolvePhase(phase, out var resolvedPhase))
            {
                return BadRequest(new { message = $"Unsupported phase '{phase}'." });
            }

            try
            {
                return Ok(await _applyAiService.RetryPhaseAsync(User, jobId, resolvedPhase, request, cancellationToken));
            }
            catch (Exception exception)
            {
                return MapException(exception);
            }
        }

        private static bool TryResolvePhase(string routeValue, out PipelinePhase phase)
        {
            if (Enum.TryParse(routeValue, ignoreCase: true, out phase))
            {
                return true;
            }

            var match = PipelinePhaseCatalog.All.FirstOrDefault(item => string.Equals(item.RouteSegment, routeValue, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                phase = match.Phase;
                return true;
            }

            phase = default;
            return false;
        }

        private bool RequestWantsServerSentEvents()
        {
            return Request.Headers.Accept.ToString().Contains("text/event-stream", StringComparison.OrdinalIgnoreCase);
        }

        private async Task StreamEventsAsync(string jobId, CancellationToken cancellationToken)
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache, no-transform";
            Response.Headers.Append("Connection", "keep-alive");
            Response.Headers.Append("X-Accel-Buffering", "no");

            await Response.StartAsync(cancellationToken);
            await Response.WriteAsync("retry: 2000\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);

            var lastEventId = ResolveLastEventId();
            var lastKeepAliveAtUtc = DateTimeOffset.UtcNow;
            var pollDelay = TimeSpan.FromSeconds(1);

            while (!cancellationToken.IsCancellationRequested)
            {
                var events = await _applyAiService.GetEventsAsync(User, jobId, cancellationToken);
                var unsentEvents = GetUnsentEvents(events, lastEventId);

                foreach (var pipelineEvent in unsentEvents)
                {
                    await WriteEventAsync(pipelineEvent, cancellationToken);
                    lastEventId = pipelineEvent.EventId;
                    lastKeepAliveAtUtc = DateTimeOffset.UtcNow;
                }

                var job = await _applyAiService.GetJobAsync(User, jobId, cancellationToken);
                if (job.Status is PipelineJobStatus.Completed or PipelineJobStatus.Failed)
                {
                    var finalEvents = await _applyAiService.GetEventsAsync(User, jobId, cancellationToken);
                    foreach (var pipelineEvent in GetUnsentEvents(finalEvents, lastEventId))
                    {
                        await WriteEventAsync(pipelineEvent, cancellationToken);
                        lastEventId = pipelineEvent.EventId;
                    }

                    await Response.Body.FlushAsync(cancellationToken);
                    return;
                }

                if (DateTimeOffset.UtcNow - lastKeepAliveAtUtc >= TimeSpan.FromSeconds(15))
                {
                    await Response.WriteAsync(": keepalive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    lastKeepAliveAtUtc = DateTimeOffset.UtcNow;
                }

                await Task.Delay(pollDelay, cancellationToken);
            }
        }

        private string? ResolveLastEventId()
        {
            var lastEventId = Request.Headers["Last-Event-ID"].ToString();
            if (!string.IsNullOrWhiteSpace(lastEventId))
            {
                return lastEventId;
            }

            lastEventId = Request.Query["lastEventId"].ToString();
            return string.IsNullOrWhiteSpace(lastEventId) ? null : lastEventId;
        }

        private static IReadOnlyList<PipelineEventEnvelope> GetUnsentEvents(
            IReadOnlyList<PipelineEventEnvelope> events,
            string? lastEventId)
        {
            if (string.IsNullOrWhiteSpace(lastEventId))
            {
                return events;
            }

            var lastSentIndex = -1;
            for (var index = 0; index < events.Count; index++)
            {
                if (string.Equals(events[index].EventId, lastEventId, StringComparison.Ordinal))
                {
                    lastSentIndex = index;
                    break;
                }
            }

            if (lastSentIndex < 0)
            {
                return events;
            }

            var unsentEvents = new List<PipelineEventEnvelope>(events.Count - lastSentIndex - 1);
            for (var index = lastSentIndex + 1; index < events.Count; index++)
            {
                unsentEvents.Add(events[index]);
            }

            return unsentEvents;
        }

        private async Task WriteEventAsync(PipelineEventEnvelope pipelineEvent, CancellationToken cancellationToken)
        {
            await Response.WriteAsync($"id: {pipelineEvent.EventId}\n", cancellationToken);
            await Response.WriteAsync($"data: {JsonSerializer.Serialize(pipelineEvent)}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        private IActionResult MapException(Exception exception)
        {
            return exception switch
            {
                KeyNotFoundException => NotFound(new { message = exception.Message }),
                ArgumentException => BadRequest(new { message = exception.Message }),
                JsonException => BadRequest(new { message = exception.Message }),
                InvalidOperationException => Conflict(new { message = exception.Message }),
                _ => StatusCode(StatusCodes.Status500InternalServerError, new { message = "An unexpected error occurred.", detail = exception.Message })
            };
        }
    }
}