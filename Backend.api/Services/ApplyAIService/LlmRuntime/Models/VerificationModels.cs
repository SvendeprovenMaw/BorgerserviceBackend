namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public enum VerificationStage
{
    Requirements,
    CandidateEvidence,
    Matching,
    ApplicationGeneration
}

public sealed class StageVerificationRequest
{
    public VerificationStage Stage { get; init; }

    public string DocumentId { get; init; } = string.Empty;

    public string DocumentJson { get; init; } = string.Empty;

    public string OutputSchemaPath { get; init; } = string.Empty;

    public List<string> ExpectedParsedFiles { get; init; } = [];

    public List<string> AllowedCitationFiles { get; init; } = [];

    public List<string> DisallowedCitationFiles { get; init; } = [];

    public string? ExpectedRequirementsDocumentId { get; init; }

    public string? ExpectedCandidateEvidenceDocumentId { get; init; }

    public string? ExpectedMatchingDocumentId { get; init; }

    public string? ExpectedCompanyContextDocumentId { get; init; }

    public string? ExpectedApplicationDocumentId { get; init; }

    public string? RequirementsDocumentJson { get; init; }

    public string? CandidateEvidenceDocumentJson { get; init; }

    public string? MatchingDocumentJson { get; init; }

    public int? MaxMainContentCharacters { get; init; }

    public int? EstimatedCharactersPerLine { get; init; }
}

public sealed class VerificationFinding
{
    public string RuleId { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string SubjectType { get; init; } = string.Empty;

    public string? SubjectId { get; init; }

    public string MessageDa { get; init; } = string.Empty;

    public bool BlockingForDownstream { get; init; }
}

public sealed class StageVerificationResult
{
    public string Stage { get; init; } = string.Empty;

    public string DocumentId { get; init; } = string.Empty;

    public string VerificationMode { get; init; } = "mechanical_only";

    public string Status { get; init; } = string.Empty;

    public bool ApprovedForDownstream { get; init; }

    public int WarningCount { get; init; }

    public int ErrorCount { get; init; }

    public string ArtifactPath { get; init; } = string.Empty;

    public string GateArtifactPath { get; init; } = string.Empty;

    public GateEvaluationResult Gate { get; init; } = new();

    public List<VerificationFinding> Findings { get; init; } = [];
}

public sealed class GateEvaluationResult
{
    public string Stage { get; init; } = string.Empty;

    public string Decision { get; init; } = "continue";

    public bool ApprovedForDownstream { get; init; }

    public int HardInvalidCount { get; init; }

    public int SoftQualityCount { get; init; }

    public string SummaryDa { get; init; } = string.Empty;

    public List<string> BlockingReasons { get; init; } = [];

    public List<string> AdvisoryReasons { get; init; } = [];

    public List<GateMetricResult> Metrics { get; init; } = [];
}

public sealed class GateMetricResult
{
    public string MetricKey { get; init; } = string.Empty;

    public string Operator { get; init; } = string.Empty;

    public string Actual { get; init; } = string.Empty;

    public string Expected { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public string MessageDa { get; init; } = string.Empty;
}