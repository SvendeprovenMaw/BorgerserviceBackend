using System.Security.Claims;
using ApplyAI.LlmPipeline;

namespace Backend.api.Services.ApplyAIService
{
    public interface IApplyAIService
    {
        Task<PipelineJobAcceptedResponse> SubmitJobAsync(ClaimsPrincipal claimsPrincipal, ApplyAiJobRequest request, CancellationToken cancellationToken = default);
        Task<PipelineJobAcceptedResponse> SubmitUploadedJobAsync(ClaimsPrincipal claimsPrincipal, ApplyAiJobUploadRequest request, CancellationToken cancellationToken = default);
        Task<PipelineJobAcceptedResponse> SubmitLinkedJobAsync(ClaimsPrincipal claimsPrincipal, ApplyAiJobLinkRequest request, CancellationToken cancellationToken = default);
        Task<PipelineJobSnapshot> GetJobAsync(ClaimsPrincipal claimsPrincipal, string jobId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PipelineEventEnvelope>> GetEventsAsync(ClaimsPrincipal claimsPrincipal, string jobId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<PipelineArtifactReference>> GetArtifactsAsync(ClaimsPrincipal claimsPrincipal, string jobId, CancellationToken cancellationToken = default);
        Task<ApplyAiArtifactContentResponse> GetArtifactContentAsync(ClaimsPrincipal claimsPrincipal, string jobId, string artifactId, CancellationToken cancellationToken = default);
        Task<ApplyAiPhaseDocumentResponse> GetPhaseDocumentAsync(ClaimsPrincipal claimsPrincipal, string jobId, PipelinePhase phase, CancellationToken cancellationToken = default);
        Task<ApplyAiPhaseDocumentResponse> UpdatePhaseDocumentAsync(ClaimsPrincipal claimsPrincipal, string jobId, PipelinePhase phase, ApplyAiPhaseDocumentUpdateRequest request, CancellationToken cancellationToken = default);
        Task<PipelineJobSnapshot> ApprovePhaseAsync(ClaimsPrincipal claimsPrincipal, string jobId, PipelinePhase phase, ApplyAiPhaseApprovalRequest? request = null, CancellationToken cancellationToken = default);
        Task<PipelineJobSnapshot> RetryPhaseAsync(ClaimsPrincipal claimsPrincipal, string jobId, PipelinePhase phase, ApplyAiPhaseRetryRequest? request = null, CancellationToken cancellationToken = default);
    }
}