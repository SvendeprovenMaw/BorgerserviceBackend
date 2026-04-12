using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Performs conservative, local repairs when matching output overstates confidence without evidence.
/// </summary>
public sealed class MatchingDeterministicRepairService : IMatchingDeterministicRepairService
{
    private static readonly JsonSerializerOptions RepairJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Downgrades unsupported match claims instead of inventing evidence or rewriting the whole document.
    /// </summary>
    public Task<MatchingRepairResult> RepairAsync(string matchingJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(matchingJson))
        {
            return Task.FromResult(new MatchingRepairResult
            {
                WasModified = false,
                SummaryDa = "Matching-output var tomt, så ingen deterministisk repair kunne udføres.",
                RepairedJson = matchingJson
            });
        }

        var rootNode = JsonNode.Parse(matchingJson) as JsonObject;
        if (rootNode is null)
        {
            return Task.FromResult(new MatchingRepairResult
            {
                WasModified = false,
                SummaryDa = "Matching-output kunne ikke parses som et JSON-objekt, så ingen deterministisk repair kunne udføres.",
                RepairedJson = matchingJson
            });
        }

        var actions = new List<MatchingRepairAction>();
        if (rootNode["matches"] is JsonArray matches)
        {
            // Only touch matches that have no support at all; everything else is left untouched.
            foreach (var matchNode in matches.OfType<JsonObject>())
            {
                var requirementId = matchNode["requirement_id"]?.GetValue<string>() ?? string.Empty;
                var matchedEvidenceIds = matchNode["matched_evidence_ids"] as JsonArray;
                var matchedEvidenceCount = matchedEvidenceIds?.Count ?? 0;

                if (matchedEvidenceCount > 0)
                {
                    continue;
                }

                var confidence = matchNode["confidence"]?.GetValue<string>() ?? string.Empty;
                if (string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase))
                {
                    matchNode["confidence"] = "low";
                    actions.Add(new MatchingRepairAction
                    {
                        RequirementId = requirementId,
                        ActionType = "downgrade_confidence",
                        MessageDa = "Confidence blev nedjusteret fra high til low, fordi kravet ikke havde understøttende evidence_ids."
                    });
                }

                var verdict = matchNode["verdict"]?.GetValue<string>() ?? string.Empty;
                if (string.Equals(verdict, "matched", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(verdict, "partially_matched", StringComparison.OrdinalIgnoreCase))
                {
                    matchNode["verdict"] = "unclear";
                    actions.Add(new MatchingRepairAction
                    {
                        RequirementId = requirementId,
                        ActionType = "downgrade_verdict",
                        MessageDa = "Verdict blev ændret til unclear, fordi kravet stod som matchet uden understøttende evidence_ids."
                    });
                }

                var needsHumanReview = matchNode["needs_human_review"]?.GetValue<bool>() ?? false;
                if (!needsHumanReview)
                {
                    matchNode["needs_human_review"] = true;
                    actions.Add(new MatchingRepairAction
                    {
                        RequirementId = requirementId,
                        ActionType = "require_human_review",
                        MessageDa = "needs_human_review blev sat til true, fordi kravet stod uden understøttende evidence_ids."
                    });
                }
            }
        }

        return Task.FromResult(new MatchingRepairResult
        {
            WasModified = actions.Count > 0,
            SummaryDa = actions.Count > 0
                ? $"Deterministisk matching-repair anvendte {actions.Count} konservative ændringer."
                : "Ingen deterministiske matching-repairs var nødvendige.",
            RepairedJson = rootNode.ToJsonString(RepairJsonOptions),
            AppliedActions = actions
        });
    }
}