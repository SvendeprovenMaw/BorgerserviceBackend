using System.Text.Json;
using ApplyAI.LlmPipeline;

namespace Backend.api.Services.ApplyAIService
{
    public static class ApplyAiSnapshotFactory
    {
        private const string RoutePrefix = "/api/ai/pipeline/jobs";
        private const int RecommendedPollIntervalSeconds = 2;

        public static PipelineJobAcceptedResponse CreateAcceptedResponse(ApplyAiPipelineJob job)
        {
            return new PipelineJobAcceptedResponse(
                job.Id.ToString("N"),
                job.WorkflowMode,
                BuildStatusUrl(job.Id),
                BuildEventsUrl(job.Id),
                $"{BuildStatusUrl(job.Id)}/phases/{{phase}}/approve",
                $"{BuildStatusUrl(job.Id)}/phases/{{phase}}/retry",
                RecommendedPollIntervalSeconds,
                true,
                job.CreatedAtUtc);
        }

        public static PipelineJobSnapshot CreateSnapshot(ApplyAiPipelineJob job)
        {
            var sortedPhases = job.PhaseStates.OrderBy(item => PipelinePhaseCatalog.IndexOf(item.Phase)).ToArray();
            return new PipelineJobSnapshot(
                job.Id.ToString("N"),
                job.WorkflowMode,
                job.Status,
                job.CurrentPhase,
                job.CurrentActivity,
                job.StatusMessage,
                job.ProgressPercent,
                BuildStatusUrl(job.Id),
                BuildEventsUrl(job.Id),
                RecommendedPollIntervalSeconds,
                job.CreatedAtUtc,
                job.UpdatedAtUtc,
                job.CompletedAtUtc,
                BuildAvailableActions(job),
                BuildArtifactReferences(job.Artifacts),
                sortedPhases.Select(CreatePhaseSnapshot).ToArray());
        }

        public static IReadOnlyList<PipelineEventEnvelope> CreateEvents(ApplyAiPipelineJob job)
        {
            return job.Events
                .OrderBy(item => item.OccurredAtUtc)
                .ThenBy(item => item.EventId, StringComparer.Ordinal)
                .Select(item => new PipelineEventEnvelope(
                    item.EventId,
                    item.EventType,
                    job.Id.ToString("N"),
                    item.JobStatus,
                    item.Phase,
                    item.Activity,
                    item.ProgressPercent,
                    item.Message,
                    item.OccurredAtUtc))
                .ToArray();
        }

        public static IReadOnlyList<PipelineArtifactReference> CreateArtifacts(ApplyAiPipelineJob job)
        {
            return BuildArtifactReferences(job.Artifacts);
        }

        public static ApplyAiPhaseDocumentResponse CreatePhaseDocumentResponse(ApplyAiPipelineJob job, ApplyAiPipelinePhaseState phaseState)
        {
            var isEditable = job.WorkflowMode == PipelineWorkflowMode.Manual
                && job.Status == PipelineJobStatus.AwaitingUserAction
                && job.CurrentPhase == phaseState.Phase;

            return new ApplyAiPhaseDocumentResponse(
                phaseState.Phase,
                phaseState.DocumentId ?? string.Empty,
                ParseJsonOrDefault(phaseState.DocumentJson, "{}"),
                ParseJsonOrDefault(phaseState.VerificationJson, "{}"),
                ParseJsonOrDefault(phaseState.GateJson, "{}"),
                isEditable,
                phaseState.ApprovedForDownstream,
                phaseState.HasUnverifiedEdits,
                BuildArtifactReferences(job.Artifacts.Where(item => item.Phase == phaseState.Phase)));
        }

        private static PipelinePhaseSnapshot CreatePhaseSnapshot(ApplyAiPipelinePhaseState phaseState)
        {
            var definition = PipelinePhaseCatalog.Get(phaseState.Phase);
            return new PipelinePhaseSnapshot(
                phaseState.Phase,
                definition.DisplayName,
                phaseState.Status,
                phaseState.CurrentActivity,
                phaseState.StatusMessage,
                phaseState.AttemptCount,
                phaseState.RepairAttemptCount,
                phaseState.WarningCount,
                phaseState.ErrorCount,
                phaseState.ApprovalRequired,
                phaseState.StartedAtUtc,
                phaseState.CompletedAtUtc,
                phaseState.ApprovedAtUtc,
                BuildArtifactReferences(phaseState.Job.Artifacts.Where(item => item.Phase == phaseState.Phase)));
        }

        private static PipelineActionLink[] BuildAvailableActions(ApplyAiPipelineJob job)
        {
            if (job.Status == PipelineJobStatus.AwaitingUserAction && job.CurrentPhase.HasValue)
            {
                var routeSegment = PipelinePhaseCatalog.ToRouteSegment(job.CurrentPhase.Value);
                return
                [
                    new PipelineActionLink(
                        PipelineActionKind.ApprovePhase,
                        "Approve and continue",
                        $"{BuildStatusUrl(job.Id)}/phases/{routeSegment}/approve",
                        "POST",
                        job.CurrentPhase),
                    new PipelineActionLink(
                        PipelineActionKind.RetryPhase,
                        "Retry phase",
                        $"{BuildStatusUrl(job.Id)}/phases/{routeSegment}/retry",
                        "POST",
                        job.CurrentPhase),
                ];
            }

            if (job.Status == PipelineJobStatus.Failed && job.CurrentPhase.HasValue)
            {
                var routeSegment = PipelinePhaseCatalog.ToRouteSegment(job.CurrentPhase.Value);
                return
                [
                    new PipelineActionLink(
                        PipelineActionKind.RetryPhase,
                        "Retry phase",
                        $"{BuildStatusUrl(job.Id)}/phases/{routeSegment}/retry",
                        "POST",
                        job.CurrentPhase),
                ];
            }

            return [];
        }

        private static PipelineArtifactReference[] BuildArtifactReferences(IEnumerable<ApplyAiPipelineArtifact> artifacts)
        {
            return artifacts
                .OrderBy(item => item.Phase.HasValue ? PipelinePhaseCatalog.IndexOf(item.Phase.Value) : int.MaxValue)
                .ThenByDescending(item => item.IsPrimary)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(item => new PipelineArtifactReference(
                    item.ArtifactKind,
                    BuildArtifactUrl(item.JobId, item.Id),
                    item.DisplayName,
                    item.MediaType,
                    item.Phase,
                    item.IsPrimary))
                .ToArray();
        }

        private static JsonElement ParseJsonOrDefault(string? json, string fallbackJson)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? fallbackJson : json);
            return document.RootElement.Clone();
        }

        private static string BuildStatusUrl(Guid jobId) => $"{RoutePrefix}/{jobId:N}";

        private static string BuildEventsUrl(Guid jobId) => $"{BuildStatusUrl(jobId)}/events";

        private static string BuildArtifactUrl(Guid jobId, Guid artifactId) => $"{BuildStatusUrl(jobId)}/artifacts/{artifactId:N}/content";
    }
}