using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Repairs structural inconsistencies inside the generated application without changing its overall narrative intent.
/// </summary>
public sealed class ApplicationGenerationDeterministicRepairService : IApplicationGenerationDeterministicRepairService
{
    private static readonly JsonSerializerOptions RepairJsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Fixes broken references between claims, sections, evidence, and requirements by using only verified upstream IDs.
    /// </summary>
    public Task<ApplicationGenerationRepairResult> RepairAsync(
        string applicationJson,
        string requirementsJson,
        string candidateEvidenceJson,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(applicationJson))
        {
            return Task.FromResult(new ApplicationGenerationRepairResult
            {
                WasModified = false,
                SummaryDa = "Application-generation-output var tomt, så ingen deterministisk repair kunne udføres.",
                RepairedJson = applicationJson
            });
        }

        var rootNode = JsonNode.Parse(applicationJson) as JsonObject;
        if (rootNode is null)
        {
            return Task.FromResult(new ApplicationGenerationRepairResult
            {
                WasModified = false,
                SummaryDa = "Application-generation-output kunne ikke parses som et JSON-objekt, så ingen deterministisk repair kunne udføres.",
                RepairedJson = applicationJson
            });
        }

        if (rootNode["claim_register"] is not JsonArray claims || rootNode["sections"] is not JsonArray sections)
        {
            return Task.FromResult(new ApplicationGenerationRepairResult
            {
                WasModified = false,
                SummaryDa = "Application-generation-output manglede claim_register eller sections, så ingen deterministisk repair kunne udføres.",
                RepairedJson = rootNode.ToJsonString(RepairJsonOptions)
            });
        }

        var validRequirementIds = ExtractIds(requirementsJson, "requirements", "requirement_id");
        var validEvidenceIds = ExtractIds(candidateEvidenceJson, "evidence_items", "evidence_id");
        var actions = new List<ApplicationGenerationRepairAction>();
        var claimsById = BuildObjectIndex(claims, "claim_id");
        var sectionsById = BuildObjectIndex(sections, "section_id");

        // First prune impossible references so later reciprocity repairs work with a clean graph.
        foreach (var (sectionId, section) in sectionsById)
        {
            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(section, "claim_ids"),
                allowedValues: claimsById.Keys,
                subjectType: "section",
                subjectId: sectionId,
                actionType: "prune_invalid_claim_references",
                messageDa: "Ugyldige eller duplikerede claim_ids blev fjernet fra sektionen.",
                actions: actions);

            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(section, "supported_requirement_ids"),
                allowedValues: validRequirementIds,
                subjectType: "section",
                subjectId: sectionId,
                actionType: "prune_invalid_requirement_references",
                messageDa: "Ugyldige eller duplikerede supported_requirement_ids blev fjernet fra sektionen.",
                actions: actions);

            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(section, "supported_evidence_ids"),
                allowedValues: validEvidenceIds,
                subjectType: "section",
                subjectId: sectionId,
                actionType: "prune_invalid_evidence_references",
                messageDa: "Ugyldige eller duplikerede supported_evidence_ids blev fjernet fra sektionen.",
                actions: actions);
        }

            // Then restore missing reciprocal links between claim_register and sections.
        foreach (var (claimId, claim) in claimsById)
        {
            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(claim, "section_ids"),
                allowedValues: sectionsById.Keys,
                subjectType: "claim",
                subjectId: claimId,
                actionType: "prune_invalid_section_references",
                messageDa: "Ugyldige eller duplikerede section_ids blev fjernet fra claimet.",
                actions: actions);

            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(claim, "requirement_ids"),
                allowedValues: validRequirementIds,
                subjectType: "claim",
                subjectId: claimId,
                actionType: "prune_invalid_requirement_references",
                messageDa: "Ugyldige eller duplikerede requirement_ids blev fjernet fra claimet.",
                actions: actions);

            RemoveUnknownAndDuplicateValues(
                array: GetOrCreateArray(claim, "evidence_ids"),
                allowedValues: validEvidenceIds,
                subjectType: "claim",
                subjectId: claimId,
                actionType: "prune_invalid_evidence_references",
                messageDa: "Ugyldige eller duplikerede evidence_ids blev fjernet fra claimet.",
                actions: actions);
        }

        foreach (var (claimId, claim) in claimsById)
        {
            foreach (var sectionId in ReadStringValues(GetOrCreateArray(claim, "section_ids")))
            {
                if (!sectionsById.TryGetValue(sectionId, out var section))
                {
                    continue;
                }

                var sectionClaimIds = GetOrCreateArray(section, "claim_ids");
                if (ContainsValue(sectionClaimIds, claimId))
                {
                    continue;
                }

                sectionClaimIds.Add(claimId);
                actions.Add(new ApplicationGenerationRepairAction
                {
                    SubjectType = "claim",
                    SubjectId = claimId,
                    ActionType = "add_reciprocal_section_claim_reference",
                    MessageDa = $"Claimet blev tilføjet til sektionen '{sectionId}', så relationen blev gensidigt konsistent."
                });
            }
        }

        foreach (var (sectionId, section) in sectionsById)
        {
            foreach (var claimId in ReadStringValues(GetOrCreateArray(section, "claim_ids")))
            {
                if (!claimsById.TryGetValue(claimId, out var claim))
                {
                    continue;
                }

                var claimSectionIds = GetOrCreateArray(claim, "section_ids");
                if (ContainsValue(claimSectionIds, sectionId))
                {
                    continue;
                }

                claimSectionIds.Add(sectionId);
                actions.Add(new ApplicationGenerationRepairAction
                {
                    SubjectType = "section",
                    SubjectId = sectionId,
                    ActionType = "add_reciprocal_claim_section_reference",
                    MessageDa = $"Sektionen blev tilføjet til claimet '{claimId}', så relationen blev gensidigt konsistent."
                });
            }
        }

        // Finally, infer missing requirement/evidence links only from section support or selected strategy IDs.
        var selectedRequirementIds = GetStrategyIds(rootNode, "selected_requirement_ids", validRequirementIds);
        var selectedEvidenceIds = GetStrategyIds(rootNode, "selected_evidence_ids", validEvidenceIds);

        foreach (var (claimId, claim) in claimsById)
        {
            var claimKind = GetStringValue(claim["claim_kind"]);
            var claimEvidenceIds = GetOrCreateArray(claim, "evidence_ids");
            var claimRequirementIds = GetOrCreateArray(claim, "requirement_ids");

            if (RequiresEvidence(claimKind) && claimEvidenceIds.Count == 0)
            {
                var inferredEvidenceIds = CollectReferencedIds(
                    claim,
                    referencePropertyName: "section_ids",
                    sectionsById,
                    sourcePropertyName: "supported_evidence_ids",
                    selectedEvidenceIds);

                if (TryAppendMissingValues(claimEvidenceIds, inferredEvidenceIds))
                {
                    actions.Add(new ApplicationGenerationRepairAction
                    {
                        SubjectType = "claim",
                        SubjectId = claimId,
                        ActionType = "infer_missing_evidence_ids",
                        MessageDa = "Manglede evidence_ids blev udfyldt ud fra de understøttende sektioner og den valgte ansøgningsstrategi."
                    });
                }
            }

            if (string.Equals(claimKind, "role_alignment", StringComparison.OrdinalIgnoreCase) && claimRequirementIds.Count == 0)
            {
                var inferredRequirementIds = CollectReferencedIds(
                    claim,
                    referencePropertyName: "section_ids",
                    sectionsById,
                    sourcePropertyName: "supported_requirement_ids",
                    selectedRequirementIds);

                if (TryAppendMissingValues(claimRequirementIds, inferredRequirementIds))
                {
                    actions.Add(new ApplicationGenerationRepairAction
                    {
                        SubjectType = "claim",
                        SubjectId = claimId,
                        ActionType = "infer_missing_requirement_ids",
                        MessageDa = "Manglede requirement_ids blev udfyldt ud fra de understøttende sektioner og den valgte ansøgningsstrategi."
                    });
                }
            }
        }

        return Task.FromResult(new ApplicationGenerationRepairResult
        {
            WasModified = actions.Count > 0,
            SummaryDa = actions.Count > 0
                ? $"Deterministisk application-generation-repair anvendte {actions.Count} konservative ændringer."
                : "Ingen deterministiske application-generation-repairs var nødvendige.",
            RepairedJson = rootNode.ToJsonString(RepairJsonOptions),
            AppliedActions = actions
        });
    }

    private static Dictionary<string, JsonObject> BuildObjectIndex(JsonArray items, string idPropertyName)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var item in items.OfType<JsonObject>())
        {
            var id = GetStringValue(item[idPropertyName]);
            if (string.IsNullOrWhiteSpace(id) || result.ContainsKey(id))
            {
                continue;
            }

            result.Add(id, item);
        }

        return result;
    }

    private static JsonArray GetOrCreateArray(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonArray array)
        {
            return array;
        }

        var created = new JsonArray();
        parent[propertyName] = created;
        return created;
    }

    private static void RemoveUnknownAndDuplicateValues(
        JsonArray array,
        IEnumerable<string> allowedValues,
        string subjectType,
        string subjectId,
        string actionType,
        string messageDa,
        List<ApplicationGenerationRepairAction> actions)
    {
        var allowed = new HashSet<string>(allowedValues, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sanitized = new List<string>();

        foreach (var value in ReadStringValues(array))
        {
            if (!allowed.Contains(value) || !seen.Add(value))
            {
                continue;
            }

            sanitized.Add(value);
        }

        if (SequenceEqual(array, sanitized))
        {
            return;
        }

        ResetArray(array, sanitized);
        actions.Add(new ApplicationGenerationRepairAction
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            ActionType = actionType,
            MessageDa = messageDa
        });
    }

    private static List<string> CollectReferencedIds(
        JsonObject claim,
        string referencePropertyName,
        IReadOnlyDictionary<string, JsonObject> sectionsById,
        string sourcePropertyName,
        IReadOnlyList<string> fallbackIds)
    {
        var collected = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sectionId in ReadStringValues(GetOrCreateArray(claim, referencePropertyName)))
        {
            if (!sectionsById.TryGetValue(sectionId, out var section))
            {
                continue;
            }

            foreach (var value in ReadStringValues(GetOrCreateArray(section, sourcePropertyName)))
            {
                if (seen.Add(value))
                {
                    collected.Add(value);
                }
            }
        }

        if (collected.Count > 0)
        {
            return collected;
        }

        foreach (var value in fallbackIds)
        {
            if (seen.Add(value))
            {
                collected.Add(value);
            }

            if (collected.Count == 1)
            {
                break;
            }
        }

        return collected;
    }

    private static IReadOnlyList<string> GetStrategyIds(JsonObject rootNode, string propertyName, ISet<string> allowedIds)
    {
        if (rootNode["application_strategy"] is not JsonObject strategy)
        {
            return [];
        }

        return ReadStringValues(GetOrCreateArray(strategy, propertyName))
            .Where(allowedIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static bool TryAppendMissingValues(JsonArray array, IReadOnlyList<string> values)
    {
        var existing = new HashSet<string>(ReadStringValues(array), StringComparer.Ordinal);
        var modified = false;

        foreach (var value in values)
        {
            if (!existing.Add(value))
            {
                continue;
            }

            array.Add(value);
            modified = true;
        }

        return modified;
    }

    private static bool ContainsValue(JsonArray array, string expectedValue)
    {
        return ReadStringValues(array).Contains(expectedValue, StringComparer.Ordinal);
    }

    private static List<string> ReadStringValues(JsonArray array)
    {
        var result = new List<string>();

        foreach (var item in array)
        {
            var value = GetStringValue(item);
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        return result;
    }

    private static string GetStringValue(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text ?? string.Empty
            : string.Empty;
    }

    private static bool RequiresEvidence(string claimKind)
    {
        return string.Equals(claimKind, "candidate_fact", StringComparison.OrdinalIgnoreCase)
            || string.Equals(claimKind, "candidate_strength", StringComparison.OrdinalIgnoreCase)
            || string.Equals(claimKind, "role_alignment", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SequenceEqual(JsonArray array, IReadOnlyList<string> values)
    {
        var existing = ReadStringValues(array);
        return existing.SequenceEqual(values, StringComparer.Ordinal);
    }

    private static void ResetArray(JsonArray array, IReadOnlyList<string> values)
    {
        array.Clear();
        foreach (var value in values)
        {
            array.Add(value);
        }
    }

    private static HashSet<string> ExtractIds(string? documentJson, string collectionPropertyName, string idPropertyName)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(documentJson);
        if (!document.RootElement.TryGetProperty(collectionPropertyName, out var collection) || collection.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in collection.EnumerateArray())
        {
            if (item.TryGetProperty(idPropertyName, out var idValue) && idValue.ValueKind == JsonValueKind.String)
            {
                var id = idValue.GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }
}