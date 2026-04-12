using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Repairs requirement identifiers so downstream references stay stable even when the model repeats IDs.
/// </summary>
public sealed class RequirementsDeterministicRepairService : IRequirementsDeterministicRepairService
{
    private static readonly JsonSerializerOptions RepairJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Replaces duplicate or missing requirement IDs with deterministic, label-based identifiers.
    /// </summary>
    public Task<RequirementsRepairResult> RepairAsync(string requirementsJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(requirementsJson))
        {
            return Task.FromResult(new RequirementsRepairResult
            {
                WasModified = false,
                SummaryDa = "Requirements-output var tomt, så ingen deterministisk repair kunne udføres.",
                RepairedJson = requirementsJson
            });
        }

        var rootNode = JsonNode.Parse(requirementsJson) as JsonObject;
        if (rootNode is null || rootNode["requirements"] is not JsonArray requirements)
        {
            return Task.FromResult(new RequirementsRepairResult
            {
                WasModified = false,
                SummaryDa = "Requirements-output kunne ikke parses med et requirements-array, så ingen deterministisk repair kunne udføres.",
                RepairedJson = requirementsJson
            });
        }

        var actions = new List<RequirementsRepairAction>();
        var usedIds = new HashSet<string>(StringComparer.Ordinal);

        // Preserve the first usable ID and only rewrite later duplicates or blanks.
        for (var index = 0; index < requirements.Count; index++)
        {
            if (requirements[index] is not JsonObject requirement)
            {
                continue;
            }

            var currentId = GetStringValue(requirement["requirement_id"]);
            if (!string.IsNullOrWhiteSpace(currentId) && usedIds.Add(currentId))
            {
                continue;
            }

            var normalizedLabel = GetStringValue(requirement["normalized_label"]);
            var replacementId = BuildUniqueRequirementId(normalizedLabel, index + 1, usedIds);
            requirement["requirement_id"] = replacementId;

            actions.Add(new RequirementsRepairAction
            {
                OriginalRequirementId = currentId,
                NewRequirementId = replacementId,
                ActionType = string.IsNullOrWhiteSpace(currentId) ? "fill_missing_requirement_id" : "replace_duplicate_requirement_id",
                MessageDa = string.IsNullOrWhiteSpace(currentId)
                    ? $"Manglede requirement_id blev udfyldt med '{replacementId}'."
                    : $"Duplikeret requirement_id '{currentId}' blev erstattet med '{replacementId}'."
            });
        }

        return Task.FromResult(new RequirementsRepairResult
        {
            WasModified = actions.Count > 0,
            SummaryDa = actions.Count > 0
                ? $"Deterministisk requirements-repair anvendte {actions.Count} konservative ændringer."
                : "Ingen deterministiske requirements-repairs var nødvendige.",
            RepairedJson = rootNode.ToJsonString(RepairJsonOptions),
            AppliedActions = actions
        });
    }

    /// <summary>
    /// Builds a readable requirement ID while guaranteeing uniqueness within the repaired document.
    /// </summary>
    private static string BuildUniqueRequirementId(string normalizedLabel, int sequenceNumber, ISet<string> usedIds)
    {
        var sanitizedBase = SanitizeIdentifier(normalizedLabel);
        if (string.IsNullOrWhiteSpace(sanitizedBase))
        {
            sanitizedBase = $"req_{sequenceNumber}";
        }
        else if (!sanitizedBase.StartsWith("req_", StringComparison.Ordinal))
        {
            sanitizedBase = $"req_{sanitizedBase}";
        }

        var candidate = sanitizedBase;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{sanitizedBase}_{suffix++}";
        }

        return candidate;
    }

    /// <summary>
    /// Converts an arbitrary label into a safe identifier fragment.
    /// </summary>
    private static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasUnderscore = false;
                continue;
            }

            if (previousWasUnderscore)
            {
                continue;
            }

            builder.Append('_');
            previousWasUnderscore = true;
        }

        return builder.ToString().Trim('_');
    }

    private static string GetStringValue(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text ?? string.Empty
            : string.Empty;
    }
}