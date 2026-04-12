using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Turns raw verification findings into policy-driven downstream decisions for each pipeline stage.
/// </summary>
public sealed class DownstreamGateEvaluator : IDownstreamGateEvaluator
{
    private readonly VerificationOptions _options;

    public DownstreamGateEvaluator(IOptions<VerificationOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Applies stage-specific thresholds on top of mechanical verification.
    /// </summary>
    public GateEvaluationResult Evaluate(StageVerificationRequest request, StageVerificationResult verificationResult)
    {
        var stageName = ToStageName(request.Stage);
        var hardInvalidCount = verificationResult.Findings.Count(finding => finding.BlockingForDownstream);
        var softQualityCount = verificationResult.Findings.Count - hardInvalidCount;

        if (!_options.GateEnabled)
        {
            return BuildResult(
                stage: stageName,
                approvedForDownstream: verificationResult.ApprovedForDownstream,
                hardInvalidCount: hardInvalidCount,
                softQualityCount: softQualityCount,
                blockingReasons: verificationResult.ApprovedForDownstream
                    ? []
                    : ["Gate-evaluering er slået fra, men mechanical verification har allerede afvist downstream."],
                advisoryReasons: [],
                metrics: []);
        }

        if (!TryParseDocument(request.DocumentJson, out var document) || document is null)
        {
            return BuildResult(
                stage: stageName,
                approvedForDownstream: false,
                hardInvalidCount: hardInvalidCount,
                softQualityCount: softQualityCount,
                blockingReasons: ["Dokumentet kunne ikke parse's til gate-evaluering."],
                advisoryReasons: [],
                metrics: []);
        }

        using (document)
        {
            return request.Stage switch
            {
                VerificationStage.Requirements => EvaluateRequirementsGate(document.RootElement, hardInvalidCount, softQualityCount),
                VerificationStage.CandidateEvidence => EvaluateCandidateEvidenceGate(document.RootElement, verificationResult, hardInvalidCount, softQualityCount),
                VerificationStage.Matching => EvaluateMatchingGate(request, document.RootElement, verificationResult, hardInvalidCount, softQualityCount),
                VerificationStage.ApplicationGeneration => EvaluateApplicationGenerationGate(verificationResult, hardInvalidCount, softQualityCount),
                _ => BuildResult(stageName, verificationResult.ApprovedForDownstream, hardInvalidCount, softQualityCount, [], [], [])
            };
        }
    }

    // Requirements remain a hard gate because malformed extraction poisons every later stage.
    private GateEvaluationResult EvaluateRequirementsGate(
        JsonElement root,
        int hardInvalidCount,
        int softQualityCount)
    {
        var policy = _options.Stages.Requirements;
        var reasons = new List<string>();
        var metrics = new List<GateMetricResult>();
        var requirementsCount = root.TryGetProperty("requirements", out var requirements) && requirements.ValueKind == JsonValueKind.Array
            ? requirements.GetArrayLength()
            : 0;

        AddHardInvalidReasonIfNeeded(policy.BlockOnAnyHardInvalid, hardInvalidCount, reasons);
        AddMetric(
            metrics,
            reasons,
            "requirements.count",
            ">=",
            requirementsCount,
            policy.MinRequirements,
            requirementsCount >= policy.MinRequirements,
            $"Requirements-fasen skal have mindst {policy.MinRequirements} krav for at gå videre.");

        return BuildResult(
            ToStageName(VerificationStage.Requirements),
            approvedForDownstream: reasons.Count == 0,
            hardInvalidCount: hardInvalidCount,
            softQualityCount: softQualityCount,
            blockingReasons: reasons,
            advisoryReasons: [],
            metrics: metrics);
    }

    // Candidate evidence quality is advisory-first: integrity issues still block, thin evidence only lowers confidence.
    private GateEvaluationResult EvaluateCandidateEvidenceGate(
        JsonElement root,
        StageVerificationResult verificationResult,
        int hardInvalidCount,
        int softQualityCount)
    {
        var policy = _options.Stages.CandidateEvidence;
        var blockingReasons = new List<string>();
        var advisoryReasons = new List<string>();
        var metrics = new List<GateMetricResult>();

        AddHardInvalidReasonIfNeeded(policy.BlockOnAnyHardInvalid, hardInvalidCount, blockingReasons);

        var evidenceItems = root.TryGetProperty("evidence_items", out var evidenceArray) && evidenceArray.ValueKind == JsonValueKind.Array
            ? evidenceArray.EnumerateArray().ToList()
            : [];

        var invalidEvidenceIds = verificationResult.Findings
            .Where(finding => finding.BlockingForDownstream
                && string.Equals(finding.SubjectType, "evidence", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(finding.SubjectId))
            .Select(finding => finding.SubjectId!)
            .ToHashSet(StringComparer.Ordinal);

        var approvedEvidence = evidenceItems
            .Where(evidence =>
            {
                var evidenceId = GetString(evidence, "evidence_id");
                return !string.IsNullOrWhiteSpace(evidenceId) && !invalidEvidenceIds.Contains(evidenceId);
            })
            .ToList();

        var totalItems = evidenceItems.Count;
        var approvedItems = approvedEvidence.Count;
        var discardedItems = totalItems - approvedItems;
        var discardRatio = totalItems == 0 ? 1d : discardedItems / (double)totalItems;
        var coveredRequirements = approvedEvidence
            .SelectMany(evidence => GetStringArray(evidence, "relevant_requirement_ids"))
            .Distinct(StringComparer.Ordinal)
            .Count();
        var strongOrMediumItems = approvedEvidence.Count(evidence =>
        {
            var strength = GetString(evidence, "strength");
            return string.Equals(strength, "strong", StringComparison.OrdinalIgnoreCase)
                || string.Equals(strength, "medium", StringComparison.OrdinalIgnoreCase);
        });

        AddMetric(
            metrics,
            advisoryReasons,
            "candidate_evidence.approved_items",
            ">=",
            approvedItems,
            policy.MinApprovedItems,
            approvedItems >= policy.MinApprovedItems,
            $"Candidate evidence skal have mindst {policy.MinApprovedItems} godkendte evidensobjekter.");
        AddMetric(
            metrics,
            advisoryReasons,
            "candidate_evidence.discard_ratio",
            "<=",
            discardRatio,
            policy.MaxDiscardRatio,
            discardRatio <= policy.MaxDiscardRatio,
            $"For stor andel af candidate evidence blev afvist. Maksimal discard ratio er {policy.MaxDiscardRatio:0.##}.");
        AddMetric(
            metrics,
            advisoryReasons,
            "candidate_evidence.covered_requirements",
            ">=",
            coveredRequirements,
            policy.MinCoveredRequirements,
            coveredRequirements >= policy.MinCoveredRequirements,
            $"Candidate evidence skal dække mindst {policy.MinCoveredRequirements} krav efter filtering.");
        AddMetric(
            metrics,
            advisoryReasons,
            "candidate_evidence.strong_or_medium_items",
            ">=",
            strongOrMediumItems,
            policy.MinStrongOrMediumItems,
            strongOrMediumItems >= policy.MinStrongOrMediumItems,
            $"Candidate evidence skal have mindst {policy.MinStrongOrMediumItems} medium eller strong evidensobjekter.");

        return BuildResult(
            ToStageName(VerificationStage.CandidateEvidence),
            approvedForDownstream: blockingReasons.Count == 0,
            hardInvalidCount: hardInvalidCount,
            softQualityCount: softQualityCount,
            blockingReasons: blockingReasons,
            advisoryReasons: advisoryReasons,
            metrics: metrics);
    }

    // Matching quality can continue with advisory output so weak fit does not behave like an automatic rejection.
    private GateEvaluationResult EvaluateMatchingGate(
        StageVerificationRequest request,
        JsonElement root,
        StageVerificationResult verificationResult,
        int hardInvalidCount,
        int softQualityCount)
    {
        var policy = _options.Stages.Matching;
        var blockingReasons = new List<string>();
        var advisoryReasons = new List<string>();
        var metrics = new List<GateMetricResult>();

        AddHardInvalidReasonIfNeeded(policy.BlockOnAnyHardInvalid, hardInvalidCount, blockingReasons);

        var totalRequirements = CountItems(request.RequirementsDocumentJson, "requirements");
        var invalidRequirementIds = verificationResult.Findings
            .Where(finding => finding.BlockingForDownstream
                && string.Equals(finding.SubjectType, "match", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(finding.SubjectId))
            .Select(finding => finding.SubjectId!)
            .ToHashSet(StringComparer.Ordinal);

        var approvedRequirementIds = root.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array
            ? matches.EnumerateArray()
                .Select(match => GetString(match, "requirement_id"))
                .Where(requirementId => !string.IsNullOrWhiteSpace(requirementId) && !invalidRequirementIds.Contains(requirementId))
                .Distinct(StringComparer.Ordinal)
                .Count()
            : 0;

        var requirementCoverageRatio = totalRequirements == 0 ? 0d : approvedRequirementIds / (double)totalRequirements;
        AddMetric(
            metrics,
            advisoryReasons,
            "matching.requirement_coverage_ratio",
            ">=",
            requirementCoverageRatio,
            policy.MinRequirementCoverageRatio,
            requirementCoverageRatio >= policy.MinRequirementCoverageRatio,
            $"Matching skal dække mindst {policy.MinRequirementCoverageRatio:0.##} af kravene med gyldige match-records.");

        var matchedWithoutEvidenceCount = verificationResult.Findings.Count(finding => string.Equals(finding.RuleId, "matching.matched_without_evidence", StringComparison.Ordinal));
        if (!policy.AllowMatchesWithoutEvidence)
        {
            AddMetric(
                metrics,
                advisoryReasons,
                "matching.matches_without_evidence",
                "==",
                matchedWithoutEvidenceCount,
                0,
                matchedWithoutEvidenceCount == 0,
                "Matched krav må ikke stå uden evidens efter gating.");
        }

        var highConfidenceWithoutEvidenceCount = verificationResult.Findings.Count(finding => string.Equals(finding.RuleId, "matching.high_confidence_without_evidence", StringComparison.Ordinal));
        if (!policy.AllowHighConfidenceWithoutEvidence)
        {
            AddMetric(
                metrics,
                advisoryReasons,
                "matching.high_confidence_without_evidence",
                "==",
                highConfidenceWithoutEvidenceCount,
                0,
                highConfidenceWithoutEvidenceCount == 0,
                "High-confidence matches må ikke stå uden evidens.");
        }

        return BuildResult(
            ToStageName(VerificationStage.Matching),
            approvedForDownstream: blockingReasons.Count == 0,
            hardInvalidCount: hardInvalidCount,
            softQualityCount: softQualityCount,
            blockingReasons: blockingReasons,
            advisoryReasons: advisoryReasons,
            metrics: metrics);
    }

    // Application generation is still a hard integrity gate because dangling claims leak into user-facing output.
    private GateEvaluationResult EvaluateApplicationGenerationGate(
        StageVerificationResult verificationResult,
        int hardInvalidCount,
        int softQualityCount)
    {
        var policy = _options.Stages.ApplicationGeneration;
        var reasons = new List<string>();
        var metrics = new List<GateMetricResult>();

        AddHardInvalidReasonIfNeeded(policy.BlockOnAnyHardInvalid, hardInvalidCount, reasons);

        var unsupportedClaims = CountUniqueSubjects(
            verificationResult.Findings,
            "application.claim_requires_evidence",
            "application.claim_evidence_reference",
            "application.claim_requirement_reference");
        var danglingRelations = CountUniqueSubjects(
            verificationResult.Findings,
            "application.claim_section_reference",
            "application.claim_section_reciprocal",
            "application.section_claim_reference");
        var unsupportedSections = CountUniqueSubjects(
            verificationResult.Findings,
            "application.section_requirement_reference",
            "application.section_evidence_reference");

        AddMetric(
            metrics,
            reasons,
            "application_generation.unsupported_claims",
            "<=",
            unsupportedClaims,
            policy.MaxUnsupportedClaims,
            unsupportedClaims <= policy.MaxUnsupportedClaims,
            "Ansøgningen har unsupported claims over den tilladte grænse.");
        AddMetric(
            metrics,
            reasons,
            "application_generation.dangling_relations",
            "<=",
            danglingRelations,
            policy.MaxDanglingRelations,
            danglingRelations <= policy.MaxDanglingRelations,
            "Ansøgningen har dangling relationer over den tilladte grænse.");
        AddMetric(
            metrics,
            reasons,
            "application_generation.unsupported_sections",
            "<=",
            unsupportedSections,
            policy.MaxUnsupportedSections,
            unsupportedSections <= policy.MaxUnsupportedSections,
            "Ansøgningen har unsupported sections over den tilladte grænse.");

        return BuildResult(
            ToStageName(VerificationStage.ApplicationGeneration),
            approvedForDownstream: reasons.Count == 0,
            hardInvalidCount: hardInvalidCount,
            softQualityCount: softQualityCount,
            blockingReasons: reasons,
            advisoryReasons: [],
            metrics: metrics);
    }

    private static void AddHardInvalidReasonIfNeeded(bool blockOnAnyHardInvalid, int hardInvalidCount, List<string> reasons)
    {
        if (blockOnAnyHardInvalid && hardInvalidCount > 0)
        {
            reasons.Add($"Fasen har {hardInvalidCount} blokkerende findings og må ikke gå downstream endnu.");
        }
    }

    private static void AddMetric(
        List<GateMetricResult> metrics,
        List<string> reasons,
        string metricKey,
        string @operator,
        int actual,
        int expected,
        bool passed,
        string failureMessage)
    {
        metrics.Add(new GateMetricResult
        {
            MetricKey = metricKey,
            Operator = @operator,
            Actual = actual.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Expected = expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Passed = passed,
            MessageDa = passed ? "OK" : failureMessage
        });

        if (!passed)
        {
            reasons.Add(failureMessage);
        }
    }

    private static void AddMetric(
        List<GateMetricResult> metrics,
        List<string> reasons,
        string metricKey,
        string @operator,
        double actual,
        double expected,
        bool passed,
        string failureMessage)
    {
        metrics.Add(new GateMetricResult
        {
            MetricKey = metricKey,
            Operator = @operator,
            Actual = actual.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            Expected = expected.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            Passed = passed,
            MessageDa = passed ? "OK" : failureMessage
        });

        if (!passed)
        {
            reasons.Add(failureMessage);
        }
    }

    /// <summary>
    /// Normalizes the final gate decision and collapses duplicate explanations.
    /// </summary>
    private static GateEvaluationResult BuildResult(
        string stage,
        bool approvedForDownstream,
        int hardInvalidCount,
        int softQualityCount,
        List<string> blockingReasons,
        List<string> advisoryReasons,
        List<GateMetricResult> metrics)
    {
        var distinctBlockingReasons = blockingReasons.Distinct(StringComparer.Ordinal).ToList();
        var distinctAdvisoryReasons = advisoryReasons.Distinct(StringComparer.Ordinal).ToList();

        return new GateEvaluationResult
        {
            Stage = stage,
            Decision = approvedForDownstream
                ? (distinctAdvisoryReasons.Count > 0 ? "continue_with_advisory" : "continue")
                : "repair_or_regenerate",
            ApprovedForDownstream = approvedForDownstream,
            HardInvalidCount = hardInvalidCount,
            SoftQualityCount = softQualityCount + distinctAdvisoryReasons.Count,
            SummaryDa = approvedForDownstream
                ? (softQualityCount + distinctAdvisoryReasons.Count > 0
                    ? $"Stage-gaten tillader fortsættelse, men fasen har stadig {softQualityCount + distinctAdvisoryReasons.Count} advisory-signaler eller ikke-blokkerende findings."
                    : "Stage-gaten bestod.")
                : string.Join(" ", distinctBlockingReasons),
            BlockingReasons = distinctBlockingReasons,
            AdvisoryReasons = distinctAdvisoryReasons,
            Metrics = metrics
        };
    }

    private static bool TryParseDocument(string? json, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int CountItems(string? json, string arrayPropertyName)
    {
        if (!TryParseDocument(json, out var document) || document is null)
        {
            return 0;
        }

        using (document)
        {
            return document.RootElement.TryGetProperty(arrayPropertyName, out var array) && array.ValueKind == JsonValueKind.Array
                ? array.GetArrayLength()
                : 0;
        }
    }

    private static int CountUniqueSubjects(IEnumerable<VerificationFinding> findings, params string[] ruleIds)
    {
        var allowedRuleIds = ruleIds.ToHashSet(StringComparer.Ordinal);
        return findings
            .Where(finding => allowedRuleIds.Contains(finding.RuleId))
            .Select(finding => $"{finding.SubjectType}:{finding.SubjectId ?? string.Empty}")
            .Distinct(StringComparer.Ordinal)
            .Count();
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

    private static string GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string ToStageName(VerificationStage stage)
    {
        return stage switch
        {
            VerificationStage.Requirements => "requirements",
            VerificationStage.CandidateEvidence => "candidate_evidence",
            VerificationStage.Matching => "matching",
            VerificationStage.ApplicationGeneration => "application_generation",
            _ => stage.ToString().ToLowerInvariant()
        };
    }
}