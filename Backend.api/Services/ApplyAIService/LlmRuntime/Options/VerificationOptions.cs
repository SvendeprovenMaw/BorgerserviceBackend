namespace Backend.api.Services.ApplyAIService.LlmRuntime.Options;

public sealed class VerificationOptions
{
    public const string SectionName = "Verification";

    public bool GateEnabled { get; set; } = true;

    public int MaxRepairAttemptsPerStage { get; set; } = 1;

    public int MaxRegenerationAttemptsPerStage { get; set; } = 1;

    public VerificationStageOptions Stages { get; set; } = new();
}

public sealed class VerificationStageOptions
{
    public RequirementsGateOptions Requirements { get; set; } = new();

    public CandidateEvidenceGateOptions CandidateEvidence { get; set; } = new();

    public MatchingGateOptions Matching { get; set; } = new();

    public ApplicationGenerationGateOptions ApplicationGeneration { get; set; } = new();
}

public sealed class RequirementsGateOptions
{
    public bool BlockOnAnyHardInvalid { get; set; } = true;

    public int MinRequirements { get; set; } = 1;
}

public sealed class CandidateEvidenceGateOptions
{
    public bool BlockOnAnyHardInvalid { get; set; } = true;

    public double MaxDiscardRatio { get; set; } = 0.40;

    public int MinApprovedItems { get; set; } = 4;

    public int MinCoveredRequirements { get; set; } = 3;

    public int MinStrongOrMediumItems { get; set; } = 2;
}

public sealed class MatchingGateOptions
{
    public bool BlockOnAnyHardInvalid { get; set; } = true;

    public double MinRequirementCoverageRatio { get; set; } = 0.75;

    public bool AllowHighConfidenceWithoutEvidence { get; set; }

    public bool AllowMatchesWithoutEvidence { get; set; }
}

public sealed class ApplicationGenerationGateOptions
{
    public bool BlockOnAnyHardInvalid { get; set; } = true;

    public int MaxUnsupportedClaims { get; set; }

    public int MaxDanglingRelations { get; set; }

    public int MaxUnsupportedSections { get; set; }
}