namespace ApplyAI.LlmPipeline;

public static class PipelinePhaseCatalog
{
    private static readonly IReadOnlyList<PipelinePhaseDefinition> Definitions =
    [
        new(
            PipelinePhase.CompanyContext,
            "Company context",
            "company-context",
            "Prompts/company_context.prompt",
            "AI Schemas/LLM Parsing/company_context_schema.json",
            null,
            false),
        new(
            PipelinePhase.Requirements,
            "Requirements",
            "requirements",
            "Prompts/requirements.prompt",
            "AI Schemas/LLM Parsing/requirements_schema.json",
            "AI Schemas/LLM Verification/requirements_verification_schema.json",
            false),
        new(
            PipelinePhase.CandidateEvidence,
            "Candidate evidence",
            "candidate-evidence",
            "Prompts/candidate_evidence.prompt",
            "AI Schemas/LLM Parsing/candidate_evidence_schema.json",
            "AI Schemas/LLM Verification/candidate_evidence_verification_schema.json",
            false),
        new(
            PipelinePhase.Matching,
            "Matching",
            "matching",
            "Prompts/matching.prompt",
            "AI Schemas/LLM Parsing/matching_schema.json",
            "AI Schemas/LLM Verification/requirement_match_verification_schema.json",
            true),
        new(
            PipelinePhase.ApplicationGeneration,
            "Application generation",
            "application-generation",
            "Prompts/application_generation.prompt",
            "AI Schemas/LLM Parsing/application_generation_schema.json",
            "AI Schemas/LLM Verification/application_generation_verification_schema.json",
            false),
    ];

    public static IReadOnlyList<PipelinePhaseDefinition> All => Definitions;

    public static PipelinePhaseDefinition Get(PipelinePhase phase)
    {
        return Definitions.Single(definition => definition.Phase == phase);
    }

    public static PipelinePhase? GetNext(PipelinePhase phase)
    {
        var index = IndexOf(phase);
        return index >= 0 && index < Definitions.Count - 1
            ? Definitions[index + 1].Phase
            : null;
    }

    public static int IndexOf(PipelinePhase phase)
    {
        for (var index = 0; index < Definitions.Count; index++)
        {
            if (Definitions[index].Phase == phase)
            {
                return index;
            }
        }

        return -1;
    }

    public static string ToRouteSegment(PipelinePhase phase)
    {
        return Get(phase).RouteSegment;
    }
}