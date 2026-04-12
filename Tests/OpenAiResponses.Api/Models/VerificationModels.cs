using System.Text.Json;

namespace OpenAiResponses.Api.Models;

/// <summary>
/// The logical phases in the sample LLM pipeline.
/// </summary>
public enum VerificationStage
{
    Requirements,
    CandidateEvidence,
    Matching,
    ApplicationGeneration
}

/// <summary>
/// Input contract for verifying one stage output against its schema and upstream dependencies.
/// </summary>
public sealed class StageVerificationRequest
{
    /// <summary>
    /// The pipeline stage being verified.
    /// </summary>
    public VerificationStage Stage { get; init; }

    /// <summary>
    /// Stable identifier expected inside the verified document.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Raw JSON emitted by the stage under verification.
    /// </summary>
    public string DocumentJson { get; init; } = string.Empty;

    /// <summary>
    /// Path to the schema file used for mechanical validation.
    /// </summary>
    public string OutputSchemaPath { get; init; } = string.Empty;

    /// <summary>
    /// Files that should appear in parsed_files metadata or citations for this run.
    /// </summary>
    public List<string> ExpectedParsedFiles { get; init; } = [];

    /// <summary>
    /// File names that are valid citation sources for this stage.
    /// </summary>
    public List<string> AllowedCitationFiles { get; init; } = [];

    /// <summary>
    /// File names that must never be cited by this stage.
    /// </summary>
    public List<string> DisallowedCitationFiles { get; init; } = [];

    /// <summary>
    /// Expected upstream requirements document identifier.
    /// </summary>
    public string? ExpectedRequirementsDocumentId { get; init; }

    /// <summary>
    /// Expected upstream candidate-evidence document identifier.
    /// </summary>
    public string? ExpectedCandidateEvidenceDocumentId { get; init; }

    /// <summary>
    /// Expected upstream matching document identifier.
    /// </summary>
    public string? ExpectedMatchingDocumentId { get; init; }

    /// <summary>
    /// Expected application document identifier for the final stage.
    /// </summary>
    public string? ExpectedApplicationDocumentId { get; init; }

    /// <summary>
    /// Upstream requirements JSON used for cross-reference checks.
    /// </summary>
    public string? RequirementsDocumentJson { get; init; }

    /// <summary>
    /// Upstream candidate-evidence JSON used for cross-reference checks.
    /// </summary>
    public string? CandidateEvidenceDocumentJson { get; init; }

    /// <summary>
    /// Upstream matching JSON used for application-stage verification.
    /// </summary>
    public string? MatchingDocumentJson { get; init; }
}

/// <summary>
/// One verifier finding produced by either schema validation or stage-specific rules.
/// </summary>
public sealed class VerificationFinding
{
    /// <summary>
    /// Stable rule identifier for the finding.
    /// </summary>
    public string RuleId { get; init; } = string.Empty;

    /// <summary>
    /// Severity of the finding, typically error or warning.
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>
    /// Logical subject type the finding belongs to.
    /// </summary>
    public string SubjectType { get; init; } = string.Empty;

    /// <summary>
    /// Optional identifier of the specific subject instance.
    /// </summary>
    public string? SubjectId { get; init; }

    /// <summary>
    /// Human-readable explanation of the finding in Danish.
    /// </summary>
    public string MessageDa { get; init; } = string.Empty;

    /// <summary>
    /// Whether the finding should prevent downstream stages from continuing.
    /// </summary>
    public bool BlockingForDownstream { get; init; }
}

/// <summary>
/// Mechanical verification result for one stage before gate evaluation is applied.
/// </summary>
public sealed class StageVerificationResult
{
    /// <summary>
    /// Stage name in response form.
    /// </summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the document that was verified.
    /// </summary>
    public string DocumentId { get; init; } = string.Empty;

    /// <summary>
    /// Label describing the verification mode used for this result.
    /// </summary>
    public string VerificationMode { get; init; } = "mechanical_only";

    /// <summary>
    /// Aggregate status derived from the findings.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Whether the stage output is currently allowed to flow downstream.
    /// </summary>
    public bool ApprovedForDownstream { get; init; }

    /// <summary>
    /// Number of warning findings.
    /// </summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// Number of error findings.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Relative path to the persisted verification artifact.
    /// </summary>
    public string ArtifactPath { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the persisted gate artifact.
    /// </summary>
    public string GateArtifactPath { get; init; } = string.Empty;

    /// <summary>
    /// Gate decision calculated from the verification result.
    /// </summary>
    public GateEvaluationResult Gate { get; init; } = new();

    /// <summary>
    /// Raw findings emitted by the verifier.
    /// </summary>
    public List<VerificationFinding> Findings { get; init; } = [];
}

/// <summary>
/// Downstream decision produced from a verified stage plus configured thresholds.
/// </summary>
public sealed class GateEvaluationResult
{
    /// <summary>
    /// Stage name this gate decision applies to.
    /// </summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>
    /// Decision outcome, such as continue, continue_with_advisory, or repair_or_regenerate.
    /// </summary>
    public string Decision { get; init; } = "continue";

    /// <summary>
    /// Whether the gate allows the next stage to run.
    /// </summary>
    public bool ApprovedForDownstream { get; init; }

    /// <summary>
    /// Count of hard-invalid conditions considered by the gate.
    /// </summary>
    public int HardInvalidCount { get; init; }

    /// <summary>
    /// Count of soft quality signals considered by the gate.
    /// </summary>
    public int SoftQualityCount { get; init; }

    /// <summary>
    /// Human-readable summary of the gate outcome in Danish.
    /// </summary>
    public string SummaryDa { get; init; } = string.Empty;

    /// <summary>
    /// Blocking reasons that prevented downstream continuation.
    /// </summary>
    public List<string> BlockingReasons { get; init; } = [];

    /// <summary>
    /// Advisory-only reasons that should be surfaced without blocking.
    /// </summary>
    public List<string> AdvisoryReasons { get; init; } = [];

    /// <summary>
    /// Individual gate metric evaluations.
    /// </summary>
    public List<GateMetricResult> Metrics { get; init; } = [];
}

/// <summary>
/// One quantitative or boolean gate check used to explain a stage decision.
/// </summary>
public sealed class GateMetricResult
{
    /// <summary>
    /// Machine-readable key for the metric.
    /// </summary>
    public string MetricKey { get; init; } = string.Empty;

    /// <summary>
    /// Operator used when comparing actual and expected values.
    /// </summary>
    public string Operator { get; init; } = string.Empty;

    /// <summary>
    /// Actual value observed during evaluation.
    /// </summary>
    public string Actual { get; init; } = string.Empty;

    /// <summary>
    /// Threshold or expected value used by the metric.
    /// </summary>
    public string Expected { get; init; } = string.Empty;

    /// <summary>
    /// Whether the metric passed.
    /// </summary>
    public bool Passed { get; init; }

    /// <summary>
    /// Human-readable metric explanation in Danish.
    /// </summary>
    public string MessageDa { get; init; } = string.Empty;
}

/// <summary>
/// Result of deterministic repair for requirements parsing output.
/// </summary>
public sealed class RequirementsRepairResult
{
    /// <summary>
    /// Whether the repair pass changed the document.
    /// </summary>
    public bool WasModified { get; init; }

    /// <summary>
    /// Human-readable summary of the repair outcome in Danish.
    /// </summary>
    public string SummaryDa { get; init; } = string.Empty;

    /// <summary>
    /// Repaired JSON document.
    /// </summary>
    public string RepairedJson { get; init; } = string.Empty;

    /// <summary>
    /// Individual repair actions that were applied.
    /// </summary>
    public List<RequirementsRepairAction> AppliedActions { get; init; } = [];
}

/// <summary>
/// One conservative rewrite applied to the requirements document.
/// </summary>
public sealed class RequirementsRepairAction
{
    /// <summary>
    /// Original identifier before repair.
    /// </summary>
    public string OriginalRequirementId { get; init; } = string.Empty;

    /// <summary>
    /// Replacement identifier after repair.
    /// </summary>
    public string NewRequirementId { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable action type.
    /// </summary>
    public string ActionType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of the repair action in Danish.
    /// </summary>
    public string MessageDa { get; init; } = string.Empty;
}

/// <summary>
/// Result of deterministic repair for requirement matching output.
/// </summary>
public sealed class MatchingRepairResult
{
    /// <summary>
    /// Whether the repair pass changed the document.
    /// </summary>
    public bool WasModified { get; init; }

    /// <summary>
    /// Human-readable summary of the repair outcome in Danish.
    /// </summary>
    public string SummaryDa { get; init; } = string.Empty;

    /// <summary>
    /// Repaired JSON document.
    /// </summary>
    public string RepairedJson { get; init; } = string.Empty;

    /// <summary>
    /// Individual repair actions that were applied.
    /// </summary>
    public List<MatchingRepairAction> AppliedActions { get; init; } = [];
}

/// <summary>
/// One conservative rewrite applied to a matching record.
/// </summary>
public sealed class MatchingRepairAction
{
    /// <summary>
    /// Requirement affected by the repair.
    /// </summary>
    public string RequirementId { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable action type.
    /// </summary>
    public string ActionType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of the repair action in Danish.
    /// </summary>
    public string MessageDa { get; init; } = string.Empty;
}

/// <summary>
/// Result of deterministic repair for the generated application document.
/// </summary>
public sealed class ApplicationGenerationRepairResult
{
    /// <summary>
    /// Whether the repair pass changed the document.
    /// </summary>
    public bool WasModified { get; init; }

    /// <summary>
    /// Human-readable summary of the repair outcome in Danish.
    /// </summary>
    public string SummaryDa { get; init; } = string.Empty;

    /// <summary>
    /// Repaired JSON document.
    /// </summary>
    public string RepairedJson { get; init; } = string.Empty;

    /// <summary>
    /// Individual repair actions that were applied.
    /// </summary>
    public List<ApplicationGenerationRepairAction> AppliedActions { get; init; } = [];
}

/// <summary>
/// One structural fix applied inside the generated application JSON.
/// </summary>
public sealed class ApplicationGenerationRepairAction
{
    /// <summary>
    /// Subject type affected by the repair.
    /// </summary>
    public string SubjectType { get; init; } = string.Empty;

    /// <summary>
    /// Identifier of the repaired subject.
    /// </summary>
    public string SubjectId { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable action type.
    /// </summary>
    public string ActionType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of the repair action in Danish.
    /// </summary>
    public string MessageDa { get; init; } = string.Empty;
}

/// <summary>
/// Feedback payload sent into a matching regeneration attempt after gate failure.
/// </summary>
public sealed class MatchingRegenerationFeedback
{
    /// <summary>
    /// Stage name this feedback belongs to.
    /// </summary>
    public string Stage { get; init; } = "matching";

    /// <summary>
    /// Gate decision that triggered regeneration.
    /// </summary>
    public string Decision { get; init; } = string.Empty;

    /// <summary>
    /// Blocking reasons from the failed gate evaluation.
    /// </summary>
    public List<string> BlockingReasons { get; init; } = [];

    /// <summary>
    /// Failed gate metrics that should steer the new generation.
    /// </summary>
    public List<GateMetricResult> FailedMetrics { get; init; } = [];

    /// <summary>
    /// Raw verifier findings from the failed attempt.
    /// </summary>
    public List<VerificationFinding> Findings { get; init; } = [];

    /// <summary>
    /// Natural-language regeneration guidance in Danish.
    /// </summary>
    public string GuidanceDa { get; init; } = string.Empty;
}

/// <summary>
/// Roll-up status for an entire pipeline run across all stages.
/// </summary>
public sealed class PipelineVerificationSummary
{
    /// <summary>
    /// Label describing the verification mode used for the pipeline.
    /// </summary>
    public string VerificationMode { get; init; } = "mechanical_only";

    /// <summary>
    /// Overall pipeline status.
    /// </summary>
    public string PipelineStatus { get; init; } = string.Empty;

    /// <summary>
    /// Stage where the pipeline stopped, if any.
    /// </summary>
    public string? StoppedAfterStage { get; init; }

    /// <summary>
    /// Suggested next action, such as continue or repair_or_regenerate.
    /// </summary>
    public string RecommendedAction { get; init; } = string.Empty;

    /// <summary>
    /// Aggregate status derived from the stage results.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Whether the full pipeline is approved for downstream consumption.
    /// </summary>
    public bool ApprovedForDownstream { get; init; }

    /// <summary>
    /// Total warning count across all stages.
    /// </summary>
    public int WarningCount { get; init; }

    /// <summary>
    /// Total error count across all stages.
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Total hard-invalid count across all gate evaluations.
    /// </summary>
    public int HardInvalidCount { get; init; }

    /// <summary>
    /// Total soft quality count across all gate evaluations.
    /// </summary>
    public int SoftQualityCount { get; init; }

    /// <summary>
    /// Per-stage verification details.
    /// </summary>
    public List<StageVerificationResult> Stages { get; init; } = [];
}

/// <summary>
/// Response returned by the verified single-job pipeline route.
/// </summary>
public sealed class PipelineWithVerificationResponse
{
    /// <summary>
    /// Relative path to the persisted run directory.
    /// </summary>
    public string RunDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Overall pipeline status.
    /// </summary>
    public string PipelineStatus { get; init; } = string.Empty;

    /// <summary>
    /// Stage where the pipeline stopped, if any.
    /// </summary>
    public string? StoppedAfterStage { get; init; }

    /// <summary>
    /// Final application document when generation completed.
    /// </summary>
    public JsonElement? ApplicationDocument { get; init; }

    /// <summary>
    /// Verification and gate details for the run.
    /// </summary>
    public PipelineVerificationSummary Verification { get; init; } = new();

    /// <summary>
    /// Optional fit advisory generated from the user's strategy preferences.
    /// </summary>
    public FitAdvisorySummary? FitAdvisory { get; init; }
}

/// <summary>
/// Non-blocking fit guidance that explains how optimistic or conservative the run was allowed to be.
/// </summary>
public sealed class FitAdvisorySummary
{
    /// <summary>
    /// Guidance mode used when framing fit.
    /// </summary>
    public string GuidanceMode { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable advisory text in Danish.
    /// </summary>
    public string SummaryDa { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the persisted advisory artifact.
    /// </summary>
    public string ArtifactPath { get; init; } = string.Empty;
}

/// <summary>
/// Response returned by the verified multi-job batch route.
/// </summary>
public sealed class MultiJobPipelineWithVerificationResponse
{
    /// <summary>
    /// Candidate directory used for all batch runs.
    /// </summary>
    public string CandidateDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Number of job listings included in the batch.
    /// </summary>
    public int JobListingCount { get; init; }

    /// <summary>
    /// Per-job summary results.
    /// </summary>
    public List<JobListingPipelineRunSummary> Jobs { get; init; } = [];
}

/// <summary>
/// Compact per-job summary used when comparing many pipeline runs at once.
/// </summary>
public sealed class JobListingPipelineRunSummary
{
    /// <summary>
    /// Source job posting file name.
    /// </summary>
    public string JobListingFileName { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the run directory for this job.
    /// </summary>
    public string RunDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Overall pipeline status for this job.
    /// </summary>
    public string PipelineStatus { get; init; } = string.Empty;

    /// <summary>
    /// Stage where the pipeline stopped, if any.
    /// </summary>
    public string? StoppedAfterStage { get; init; }

    /// <summary>
    /// Suggested next action for this run.
    /// </summary>
    public string RecommendedAction { get; init; } = string.Empty;

    /// <summary>
    /// Whether an application document was produced.
    /// </summary>
    public bool ApplicationGenerated { get; init; }

    /// <summary>
    /// Overall fit level extracted from the matching output.
    /// </summary>
    public string? OverallMatchLevel { get; init; }

    /// <summary>
    /// Human-readable matching summary in Danish.
    /// </summary>
    public string? MatchingSummaryDa { get; init; }

    /// <summary>
    /// Core application message extracted from the generated document.
    /// </summary>
    public string? ApplicationCoreMessageDa { get; init; }

    /// <summary>
    /// Guidance mode used when building the fit advisory.
    /// </summary>
    public string? FitGuidanceMode { get; init; }

    /// <summary>
    /// Human-readable fit advisory in Danish.
    /// </summary>
    public string? FitAdvisoryDa { get; init; }

    /// <summary>
    /// Relative path to the persisted fit advisory artifact.
    /// </summary>
    public string? FitAdvisoryArtifactPath { get; init; }

    /// <summary>
    /// Number of matched requirements.
    /// </summary>
    public int MatchedCount { get; init; }

    /// <summary>
    /// Number of partially matched requirements.
    /// </summary>
    public int PartiallyMatchedCount { get; init; }

    /// <summary>
    /// Number of unmatched requirements.
    /// </summary>
    public int NotMatchedCount { get; init; }

    /// <summary>
    /// Number of unclear requirements.
    /// </summary>
    public int UnclearCount { get; init; }

    /// <summary>
    /// Number of requirements selected for the final application.
    /// </summary>
    public int SelectedRequirementCount { get; init; }

    /// <summary>
    /// Number of requirements intentionally omitted from the final application.
    /// </summary>
    public int OmittedRequirementCount { get; init; }
}