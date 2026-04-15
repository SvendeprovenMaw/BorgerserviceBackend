using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Performs schema validation and stage-specific rule checks for every document emitted by the sample pipeline.
/// </summary>
public sealed class VerificationOrchestrator : IVerificationOrchestrator
{
    /// <summary>
    /// Runs validation in two passes: generic JSON/schema checks first, then domain rules for the selected stage.
    /// </summary>
    public async Task<StageVerificationResult> VerifyStageAsync(StageVerificationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<VerificationFinding>();

        if (string.IsNullOrWhiteSpace(request.DocumentJson))
        {
            findings.Add(Error("json.not_empty", "document", request.DocumentId, "Output er tomt og kan ikke verificeres."));
            return BuildResult(request, findings);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.DocumentJson);
        }
        catch (JsonException exception)
        {
            findings.Add(Error("json.parse", "document", request.DocumentId, $"Output er ikke gyldig JSON: {exception.Message}"));
            return BuildResult(request, findings);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                findings.Add(Error("json.root_object", "document", request.DocumentId, "JSON-root skal være et objekt."));
                return BuildResult(request, findings);
            }

            findings.AddRange(await ValidateAgainstSchemaAsync(request.OutputSchemaPath, document.RootElement, cancellationToken));
            findings.AddRange(EvaluateStageRules(request, document.RootElement));
        }

        return BuildResult(request, findings);
    }

    // Dispatch to the rule set that understands the structure and invariants of the current stage.
    private static IEnumerable<VerificationFinding> EvaluateStageRules(StageVerificationRequest request, JsonElement root)
    {
        return request.Stage switch
        {
            VerificationStage.Requirements => EvaluateRequirementsRules(request, root),
            VerificationStage.CandidateEvidence => EvaluateCandidateEvidenceRules(request, root),
            VerificationStage.Matching => EvaluateMatchingRules(request, root),
            VerificationStage.ApplicationGeneration => EvaluateApplicationGenerationRules(request, root),
            _ => []
        };
    }

    private static IEnumerable<VerificationFinding> EvaluateRequirementsRules(StageVerificationRequest request, JsonElement root)
    {
        var findings = new List<VerificationFinding>();
        if (!root.TryGetProperty("requirements", out var requirements) || requirements.ValueKind != JsonValueKind.Array)
        {
            findings.Add(Error("requirements.array", "document", request.DocumentId, "Requirements-array findes ikke."));
            return findings;
        }

        var requirementIds = new HashSet<string>(StringComparer.Ordinal);
        var duplicateKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var requirement in requirements.EnumerateArray())
        {
            var requirementId = GetString(requirement, "requirement_id");
            var normalizedLabel = GetString(requirement, "normalized_label");
            var requirementText = GetString(requirement, "requirement_text_da");

            if (string.IsNullOrWhiteSpace(requirementId))
            {
                findings.Add(Error("requirements.id", "requirement", null, "Et krav mangler requirement_id."));
            }
            else if (!requirementIds.Add(requirementId))
            {
                findings.Add(Error("requirements.id.unique", "requirement", requirementId, "requirement_id skal være unik."));
            }

            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                findings.Add(Error("requirements.normalized_label", "requirement", requirementId, "normalized_label må ikke være tom."));
            }

            if (string.IsNullOrWhiteSpace(requirementText))
            {
                findings.Add(Error("requirements.text", "requirement", requirementId, "requirement_text_da må ikke være tom."));
            }

            var duplicateKey = $"{normalizedLabel}::{requirementText}";
            if (!string.IsNullOrWhiteSpace(normalizedLabel) && !string.IsNullOrWhiteSpace(requirementText) && !duplicateKeys.Add(duplicateKey))
            {
                findings.Add(Error("requirements.duplicate_text", "requirement", requirementId, "To krav må ikke have samme normalized_label og identisk tekst."));
            }

            findings.AddRange(ValidateCitations(
                requirement,
                citationPropertyName: "source_citations",
                subjectType: "requirement",
                subjectId: requirementId,
                expectedParsedFiles: request.ExpectedParsedFiles,
                allowedCitationFiles: request.AllowedCitationFiles,
                disallowedCitationFiles: request.DisallowedCitationFiles,
                citationFileErrorMessage: "Citations i requirements må kun pege på jobopslaget."));
        }

        return findings;
    }

    private static IEnumerable<VerificationFinding> EvaluateCandidateEvidenceRules(StageVerificationRequest request, JsonElement root)
    {
        var findings = new List<VerificationFinding>();
        if (!root.TryGetProperty("evidence_items", out var evidenceItems) || evidenceItems.ValueKind != JsonValueKind.Array)
        {
            findings.Add(Error("candidate_evidence.array", "document", request.DocumentId, "evidence_items findes ikke."));
            return findings;
        }

        var requirementIds = ExtractIds(request.RequirementsDocumentJson, "requirements", "requirement_id");
        var evidenceIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var evidence in evidenceItems.EnumerateArray())
        {
            var evidenceId = GetString(evidence, "evidence_id");
            if (string.IsNullOrWhiteSpace(evidenceId))
            {
                findings.Add(Error("candidate_evidence.id", "evidence", null, "Et evidensobjekt mangler evidence_id."));
            }
            else if (!evidenceIds.Add(evidenceId))
            {
                findings.Add(Error("candidate_evidence.id.unique", "evidence", evidenceId, "evidence_id skal være unik."));
            }

            if (string.IsNullOrWhiteSpace(GetString(evidence, "fact_da")))
            {
                findings.Add(Error("candidate_evidence.fact", "evidence", evidenceId, "fact_da må ikke være tom."));
            }

            var relevantRequirementIds = GetStringArray(evidence, "relevant_requirement_ids");
            foreach (var requirementId in relevantRequirementIds)
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("candidate_evidence.requirement_reference", "evidence", evidenceId, $"relevant_requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            if (relevantRequirementIds.Count > 0 && string.IsNullOrWhiteSpace(GetString(evidence, "requirement_relevance_reason_da")))
            {
                findings.Add(Error("candidate_evidence.requirement_reason", "evidence", evidenceId, "requirement_relevance_reason_da må ikke være tom, når der er krav-links."));
            }

            findings.AddRange(ValidateCitations(
                evidence,
                citationPropertyName: "citations",
                subjectType: "evidence",
                subjectId: evidenceId,
                expectedParsedFiles: request.ExpectedParsedFiles,
                allowedCitationFiles: request.AllowedCitationFiles,
                disallowedCitationFiles: request.DisallowedCitationFiles,
                citationFileErrorMessage: "Citations i candidate evidence må kun pege på kandidatmateriale."));

            var supportType = GetString(evidence, "support_type");
            var strength = GetString(evidence, "strength");
            var citations = GetCitationObjects(evidence, "citations");

            if (string.Equals(supportType, "testimonial", StringComparison.OrdinalIgnoreCase)
                && citations.All(citation => !citation.FileName.Contains("reference", StringComparison.OrdinalIgnoreCase)
                    && !citation.FileName.Contains("testimonial", StringComparison.OrdinalIgnoreCase)
                    && !citation.FileName.Contains("udtalelse", StringComparison.OrdinalIgnoreCase)))
            {
                findings.Add(Warning("candidate_evidence.testimonial_source", "evidence", evidenceId, "support_type er testimonial, men citations ligner ikke en reference- eller testimonial-kilde."));
            }

            if (string.Equals(supportType, "document_metadata", StringComparison.OrdinalIgnoreCase)
                && citations.Any(citation => string.IsNullOrWhiteSpace(citation.Excerpt)))
            {
                findings.Add(Error("candidate_evidence.document_metadata_excerpt", "evidence", evidenceId, "document_metadata kræver stadig udfyldt excerpt."));
            }

            if (string.Equals(strength, "strong", StringComparison.OrdinalIgnoreCase)
                && citations.Count > 0
                && citations.All(citation => citation.Excerpt.Trim().Length < 20))
            {
                findings.Add(Warning("candidate_evidence.strong_weak_excerpt", "evidence", evidenceId, "strength er strong, men citationerne er meget korte eller uklare."));
            }
        }

        return findings;
    }

    private static IEnumerable<VerificationFinding> EvaluateMatchingRules(StageVerificationRequest request, JsonElement root)
    {
        var findings = new List<VerificationFinding>();
        var requirementIds = ExtractIds(request.RequirementsDocumentJson, "requirements", "requirement_id");
        var evidenceIds = ExtractIds(request.CandidateEvidenceDocumentJson, "evidence_items", "evidence_id");

        if (!root.TryGetProperty("matches", out var matches) || matches.ValueKind != JsonValueKind.Array)
        {
            findings.Add(Error("matching.array", "document", request.DocumentId, "matches findes ikke."));
            return findings;
        }

        foreach (var match in matches.EnumerateArray())
        {
            var requirementId = GetString(match, "requirement_id");
            if (string.IsNullOrWhiteSpace(requirementId) || !requirementIds.Contains(requirementId))
            {
                findings.Add(Error("matching.requirement_reference", "match", requirementId, "requirement_id findes ikke i krav-dokumentet."));
            }

            var matchedEvidenceIds = GetStringArray(match, "matched_evidence_ids");
            if (matchedEvidenceIds.Count != matchedEvidenceIds.Distinct(StringComparer.Ordinal).Count())
            {
                findings.Add(Error("matching.evidence_ids.unique", "match", requirementId, "matched_evidence_ids må ikke indeholde dubletter."));
            }

            foreach (var evidenceId in matchedEvidenceIds)
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    findings.Add(Error("matching.evidence_reference", "match", requirementId, $"matched_evidence_id '{evidenceId}' findes ikke i evidens-dokumentet."));
                }
            }

            if (string.IsNullOrWhiteSpace(GetString(match, "rationale_da")))
            {
                findings.Add(Error("matching.rationale", "match", requirementId, "rationale_da må ikke være tom."));
            }

            var verdict = GetString(match, "verdict");
            var confidence = GetString(match, "confidence");
            if (string.Equals(verdict, "not_matched", StringComparison.OrdinalIgnoreCase) && matchedEvidenceIds.Count > 0)
            {
                findings.Add(Warning("matching.not_matched_has_evidence", "match", requirementId, "verdict er not_matched, men matched_evidence_ids er ikke tom."));
            }

            if (string.Equals(verdict, "matched", StringComparison.OrdinalIgnoreCase) && matchedEvidenceIds.Count == 0)
            {
                findings.Add(Warning("matching.matched_without_evidence", "match", requirementId, "verdict er matched, men matched_evidence_ids er tom."));
            }

            if (string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase) && matchedEvidenceIds.Count == 0)
            {
                findings.Add(Warning("matching.high_confidence_without_evidence", "match", requirementId, "confidence er high, men matched_evidence_ids er tom."));
            }
        }

        if (root.TryGetProperty("overall_assessment", out var overallAssessment))
        {
            foreach (var evidenceId in GetStringArray(overallAssessment, "major_strength_evidence_ids"))
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    findings.Add(Error("matching.overall_strength_reference", "overall_assessment", request.DocumentId, $"major_strength_evidence_id '{evidenceId}' findes ikke i evidens-dokumentet."));
                }
            }

            var majorGapRequirementIds = GetStringArray(overallAssessment, "major_gap_requirement_ids");
            foreach (var requirementId in majorGapRequirementIds)
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("matching.overall_gap_reference", "overall_assessment", request.DocumentId, $"major_gap_requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            if (string.Equals(GetString(overallAssessment, "overall_match_level"), "strong", StringComparison.OrdinalIgnoreCase)
                && majorGapRequirementIds.Count >= 3)
            {
                findings.Add(Warning("matching.overall_level_gap_conflict", "overall_assessment", request.DocumentId, "overall_match_level er strong, men der er mange major_gap_requirement_ids."));
            }
        }

        return findings;
    }

    // Application generation is verified as a graph: claims, sections, strategy, and assembled text must agree.
    private static IEnumerable<VerificationFinding> EvaluateApplicationGenerationRules(StageVerificationRequest request, JsonElement root)
    {
        var findings = new List<VerificationFinding>();
        var requirementIds = ExtractIds(request.RequirementsDocumentJson, "requirements", "requirement_id");
        var evidenceIds = ExtractIds(request.CandidateEvidenceDocumentJson, "evidence_items", "evidence_id");

        if (root.TryGetProperty("_meta", out var meta))
        {
            ValidateExpectedId(meta, "application_document_id", request.ExpectedApplicationDocumentId, findings, request.DocumentId);
            ValidateExpectedId(meta, "requirements_document_id", request.ExpectedRequirementsDocumentId, findings, request.DocumentId);
            ValidateExpectedId(meta, "candidate_evidence_document_id", request.ExpectedCandidateEvidenceDocumentId, findings, request.DocumentId);
            ValidateExpectedId(meta, "company_context_document_id", request.ExpectedCompanyContextDocumentId, findings, request.DocumentId);
            ValidateExpectedId(meta, "matching_document_id", request.ExpectedMatchingDocumentId, findings, request.DocumentId);
        }

        var signatureName = root.TryGetProperty("_meta", out var rootMeta)
            ? GetString(rootMeta, "applicant_display_name")
            : string.Empty;

        if (request.MaxMainContentCharacters.HasValue && request.MaxMainContentCharacters.Value > 0)
        {
            var estimatedCharactersPerLine = request.EstimatedCharactersPerLine.GetValueOrDefault(CoverLetterContentMetrics.DefaultEstimatedCharactersPerLine);
            var budgetMetrics = CoverLetterContentMetrics.CalculateBudgetMetrics(root, signatureName, estimatedCharactersPerLine);
            if (budgetMetrics.BudgetUsage > request.MaxMainContentCharacters.Value)
            {
                findings.Add(Error(
                    "application.template_main_content_length",
                    "document",
                    request.DocumentId,
                    $"Ansøgningens synlige hovedtekst er {budgetMetrics.VisibleCharacterCount} rå tegn, men {budgetMetrics.ParagraphBreakCount} paragrafskift og {budgetMetrics.ExplicitLineBreakCount} interne linjeskift løfter det effektive template-forbrug til {budgetMetrics.BudgetUsage} mod maksimum {request.MaxMainContentCharacters.Value}. Dokumentet risikerer at blive klippet i PDF-layoutet."));
            }
        }

        findings.AddRange(ValidateVisibleTextScripts(root, request.DocumentId));

        var claimsById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!root.TryGetProperty("claim_register", out var claims) || claims.ValueKind != JsonValueKind.Array)
        {
            findings.Add(Error("application.claim_register", "document", request.DocumentId, "claim_register findes ikke."));
            return findings;
        }

        foreach (var claim in claims.EnumerateArray())
        {
            var claimId = GetString(claim, "claim_id");
            if (string.IsNullOrWhiteSpace(claimId))
            {
                findings.Add(Error("application.claim_id", "claim", null, "Et claim mangler claim_id."));
                continue;
            }

            if (!claimsById.TryAdd(claimId, claim))
            {
                findings.Add(Error("application.claim_id.unique", "claim", claimId, "claim_id skal være unik."));
            }

            if (string.IsNullOrWhiteSpace(GetString(claim, "claim_text_da")))
            {
                findings.Add(Error("application.claim_text", "claim", claimId, "claim_text_da må ikke være tom."));
            }

            var sectionIds = GetStringArray(claim, "section_ids");
            if (sectionIds.Count == 0)
            {
                findings.Add(Error("application.claim_sections", "claim", claimId, "section_ids må ikke være tom."));
            }

            var claimKind = GetString(claim, "claim_kind");
            var claimEvidenceIds = GetStringArray(claim, "evidence_ids");
            var claimRequirementIds = GetStringArray(claim, "requirement_ids");

            foreach (var evidenceId in claimEvidenceIds)
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    findings.Add(Error("application.claim_evidence_reference", "claim", claimId, $"claim.evidence_id '{evidenceId}' findes ikke i evidens-dokumentet."));
                }
            }

            foreach (var requirementId in claimRequirementIds)
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("application.claim_requirement_reference", "claim", claimId, $"claim.requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            if ((string.Equals(claimKind, "candidate_fact", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(claimKind, "candidate_strength", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(claimKind, "role_alignment", StringComparison.OrdinalIgnoreCase))
                && claimEvidenceIds.Count == 0)
            {
                findings.Add(Error("application.claim_requires_evidence", "claim", claimId, $"Claim-typen {claimKind} bør have mindst én evidence_id."));
            }

            if (string.Equals(claimKind, "role_alignment", StringComparison.OrdinalIgnoreCase) && claimRequirementIds.Count == 0)
            {
                findings.Add(Error("application.role_alignment_requirement", "claim", claimId, "role_alignment bør have mindst én requirement_id."));
            }
        }

        var sectionsById = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
        {
            findings.Add(Error("application.sections", "document", request.DocumentId, "sections findes ikke."));
            return findings;
        }

        foreach (var section in sections.EnumerateArray())
        {
            var sectionId = GetString(section, "section_id");
            if (string.IsNullOrWhiteSpace(sectionId))
            {
                findings.Add(Error("application.section_id", "section", null, "En sektion mangler section_id."));
                continue;
            }

            if (!sectionsById.TryAdd(sectionId, section))
            {
                findings.Add(Error("application.section_id.unique", "section", sectionId, "section_id skal være unik."));
            }

            if (string.IsNullOrWhiteSpace(GetString(section, "text_da")))
            {
                findings.Add(Error("application.section_text", "section", sectionId, "section.text_da må ikke være tom."));
            }

            var claimIds = GetStringArray(section, "claim_ids");
            var contentMode = GetString(section, "content_mode");
            if (string.Equals(contentMode, "bridge_text", StringComparison.OrdinalIgnoreCase) && claimIds.Count > 1)
            {
                findings.Add(Warning("application.bridge_claim_count", "section", sectionId, "bridge_text-sektion har mange claim_ids."));
            }

            if ((string.Equals(contentMode, "evidence_backed", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(contentMode, "mixed", StringComparison.OrdinalIgnoreCase))
                && claimIds.Count == 0)
            {
                findings.Add(Warning("application.claims_expected", "section", sectionId, "evidence_backed eller mixed-sektion bør normalt have claim_ids."));
            }

            foreach (var claimId in claimIds)
            {
                if (!claimsById.ContainsKey(claimId))
                {
                    findings.Add(Error("application.section_claim_reference", "section", sectionId, $"section.claim_id '{claimId}' findes ikke i claim_register."));
                }
            }

            foreach (var requirementId in GetStringArray(section, "supported_requirement_ids"))
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("application.section_requirement_reference", "section", sectionId, $"supported_requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            foreach (var evidenceId in GetStringArray(section, "supported_evidence_ids"))
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    findings.Add(Error("application.section_evidence_reference", "section", sectionId, $"supported_evidence_id '{evidenceId}' findes ikke i evidens-dokumentet."));
                }
            }
        }

        foreach (var (claimId, claim) in claimsById)
        {
            foreach (var sectionId in GetStringArray(claim, "section_ids"))
            {
                if (!sectionsById.TryGetValue(sectionId, out var section))
                {
                    findings.Add(Error("application.claim_section_reference", "claim", claimId, $"claim.section_id '{sectionId}' findes ikke i sections."));
                    continue;
                }

                if (!GetStringArray(section, "claim_ids").Contains(claimId, StringComparer.Ordinal))
                {
                    findings.Add(Error("application.claim_section_reciprocal", "claim", claimId, $"Relationen mellem claim '{claimId}' og section '{sectionId}' er ikke gensidigt konsistent."));
                }
            }
        }

        if (root.TryGetProperty("application_strategy", out var strategy))
        {
            foreach (var requirementId in GetStringArray(strategy, "selected_requirement_ids"))
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("application.selected_requirement_reference", "application_strategy", request.DocumentId, $"selected_requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            foreach (var evidenceId in GetStringArray(strategy, "selected_evidence_ids"))
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    findings.Add(Error("application.selected_evidence_reference", "application_strategy", request.DocumentId, $"selected_evidence_id '{evidenceId}' findes ikke i evidens-dokumentet."));
                }
            }

            var omittedRequirementIds = GetStringArray(strategy, "omitted_requirement_ids");
            foreach (var requirementId in omittedRequirementIds)
            {
                if (!requirementIds.Contains(requirementId))
                {
                    findings.Add(Error("application.omitted_requirement_reference", "application_strategy", request.DocumentId, $"omitted_requirement_id '{requirementId}' findes ikke i krav-dokumentet."));
                }
            }

            if (omittedRequirementIds.Count > 0)
            {
                var supportedRequirementIds = sectionsById.Values
                    .SelectMany(section => GetStringArray(section, "supported_requirement_ids"))
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var omittedRequirementId in omittedRequirementIds)
                {
                    if (supportedRequirementIds.Contains(omittedRequirementId))
                    {
                        findings.Add(Warning("application.omitted_requirement_conflict", "application_strategy", request.DocumentId, $"omitted_requirement_id '{omittedRequirementId}' optræder samtidig som understøttet i sections."));
                    }
                }
            }
        }

        var normalizedClaimTexts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (claimId, claim) in claimsById)
        {
            var normalizedClaimText = NormalizeWhitespace(GetString(claim, "claim_text_da")).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedClaimText) && !normalizedClaimTexts.Add(normalizedClaimText))
            {
                findings.Add(Warning("application.duplicate_claim_text", "claim", claimId, "Der findes claims med meget ens eller identisk claim_text_da."));
            }
        }

        var assembledApplication = GetString(root, "assembled_application_da");
        if (string.IsNullOrWhiteSpace(assembledApplication))
        {
            findings.Add(Error("application.assembled_text", "document", request.DocumentId, "assembled_application_da må ikke være tom."));
        }
        else
        {
            var normalizedApplication = NormalizeWhitespace(assembledApplication);
            var currentIndex = 0;
            foreach (var section in sections.EnumerateArray())
            {
                var normalizedSectionText = NormalizeWhitespace(GetString(section, "text_da"));
                if (string.IsNullOrWhiteSpace(normalizedSectionText))
                {
                    continue;
                }

                var index = normalizedApplication.IndexOf(normalizedSectionText, currentIndex, StringComparison.Ordinal);
                if (index < 0)
                {
                    findings.Add(Warning("application.assembled_sequence", "section", GetString(section, "section_id"), "Sektionsteksten kan ikke genfindes i assembled_application_da i forventet rækkefølge."));
                }
                else
                {
                    currentIndex = index + normalizedSectionText.Length;
                }
            }
        }

        return findings;
    }

    private static IEnumerable<VerificationFinding> ValidateVisibleTextScripts(JsonElement root, string documentId)
    {
        var findings = new List<VerificationFinding>();

        foreach (var candidate in EnumerateVisibleTextCandidates(root, documentId))
        {
            var suspiciousRuneSamples = FindSuspiciousLetterRuneSamples(candidate.Text);
            if (suspiciousRuneSamples.Count == 0)
            {
                continue;
            }

            findings.Add(Error(
                "application.visible_text.non_latin_script",
                candidate.SubjectType,
                candidate.SubjectId,
                $"{candidate.FieldPath} indeholder mistænkelige ikke-latinske bogstavtegn, fx {string.Join(", ", suspiciousRuneSamples)}. Ansøgningsteksten må kun bruge latinbaseret skrift med normale nordiske tegn."));
        }

        return findings;
    }

    private static IEnumerable<ApplicationVisibleTextCandidate> EnumerateVisibleTextCandidates(JsonElement root, string documentId)
    {
        if (root.TryGetProperty("application_strategy", out var strategy) && strategy.ValueKind == JsonValueKind.Object)
        {
            var subjectLine = GetString(strategy, "subject_line_da");
            if (!string.IsNullOrWhiteSpace(subjectLine))
            {
                yield return new ApplicationVisibleTextCandidate("application_strategy", documentId, "application_strategy.subject_line_da", subjectLine);
            }

            var coreMessage = GetString(strategy, "core_message_da");
            if (!string.IsNullOrWhiteSpace(coreMessage))
            {
                yield return new ApplicationVisibleTextCandidate("application_strategy", documentId, "application_strategy.core_message_da", coreMessage);
            }
        }

        var yieldedSectionText = false;
        if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                var text = GetString(section, "text_da");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                yieldedSectionText = true;
                yield return new ApplicationVisibleTextCandidate(
                    "section",
                    GetString(section, "section_id"),
                    "sections[].text_da",
                    text);
            }
        }

        if (!yieldedSectionText)
        {
            var assembledApplication = GetString(root, "assembled_application_da");
            if (!string.IsNullOrWhiteSpace(assembledApplication))
            {
                yield return new ApplicationVisibleTextCandidate("document", documentId, "assembled_application_da", assembledApplication);
            }
        }
    }

    private static List<string> FindSuspiciousLetterRuneSamples(string text)
    {
        var samples = new List<string>();
        var seenRunes = new HashSet<int>();

        foreach (var rune in text.EnumerateRunes())
        {
            if (!IsSuspiciousLetterRune(rune) || !seenRunes.Add(rune.Value))
            {
                continue;
            }

            samples.Add($"'{rune}' (U+{rune.Value:X4})");
            if (samples.Count >= 4)
            {
                break;
            }
        }

        return samples;
    }

    private static bool IsSuspiciousLetterRune(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is not UnicodeCategory.UppercaseLetter
            and not UnicodeCategory.LowercaseLetter
            and not UnicodeCategory.TitlecaseLetter
            and not UnicodeCategory.ModifierLetter
            and not UnicodeCategory.OtherLetter)
        {
            return false;
        }

        return !IsLatinLetterRune(rune);
    }

    private static bool IsLatinLetterRune(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x0041 and <= 0x005A
            or >= 0x0061 and <= 0x007A
            or 0x00AA
            or 0x00BA
            or >= 0x00C0 and <= 0x00FF
            or >= 0x0100 and <= 0x024F
            or >= 0x1E00 and <= 0x1EFF
            or >= 0x2C60 and <= 0x2C7F
            or >= 0xA720 and <= 0xA7FF
            or >= 0xAB30 and <= 0xAB6F;
    }

    private static void ValidateExpectedId(JsonElement meta, string propertyName, string? expectedValue, List<VerificationFinding> findings, string documentId)
    {
        var actualValue = GetString(meta, propertyName);
        if (string.IsNullOrWhiteSpace(actualValue))
        {
            findings.Add(Error($"application.meta.{propertyName}", "document_meta", documentId, $"{propertyName} må ikke være tom."));
            return;
        }

        if (!string.IsNullOrWhiteSpace(expectedValue) && !string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
        {
            findings.Add(Error($"application.meta.{propertyName}.match", "document_meta", documentId, $"{propertyName} matcher ikke det forventede upstream-dokument."));
        }
    }

    // Citations are the main traceability anchor, so every stage validates both presence and allowed source files.
    private static IReadOnlyList<VerificationFinding> ValidateCitations(
        JsonElement parent,
        string citationPropertyName,
        string subjectType,
        string? subjectId,
        IReadOnlyCollection<string> expectedParsedFiles,
        IReadOnlyCollection<string> allowedCitationFiles,
        IReadOnlyCollection<string> disallowedCitationFiles,
        string citationFileErrorMessage)
    {
        var findings = new List<VerificationFinding>();
        var citations = GetCitationObjects(parent, citationPropertyName);
        if (citations.Count == 0)
        {
            findings.Add(Error($"{subjectType}.citations", subjectType, subjectId, "Objektet skal have mindst én citation."));
            return findings;
        }

        foreach (var citation in citations)
        {
            if (string.IsNullOrWhiteSpace(citation.FileName))
            {
                findings.Add(Error($"{subjectType}.citation.filename", subjectType, subjectId, "Citation mangler filnavn."));
            }

            if (string.IsNullOrWhiteSpace(citation.Excerpt))
            {
                findings.Add(Error($"{subjectType}.citation.excerpt", subjectType, subjectId, "Citation excerpt må ikke være tom eller whitespace."));
            }

            if (expectedParsedFiles.Count > 0 && !expectedParsedFiles.Contains(citation.FileName, StringComparer.Ordinal))
            {
                findings.Add(Error($"{subjectType}.citation.parsed_files", subjectType, subjectId, $"Citation peger på filen '{citation.FileName}', som ikke var en del af runnet."));
            }

            if (allowedCitationFiles.Count > 0 && !allowedCitationFiles.Contains(citation.FileName, StringComparer.Ordinal))
            {
                findings.Add(Error($"{subjectType}.citation.allowed_sources", subjectType, subjectId, citationFileErrorMessage));
            }

            if (disallowedCitationFiles.Count > 0 && disallowedCitationFiles.Contains(citation.FileName, StringComparer.Ordinal))
            {
                findings.Add(Error($"{subjectType}.citation.disallowed_sources", subjectType, subjectId, citationFileErrorMessage));
            }
        }

        return findings;
    }

    // The local schema validator only implements the subset needed by these test schemas.
    private static async Task<IReadOnlyList<VerificationFinding>> ValidateAgainstSchemaAsync(string schemaPath, JsonElement instance, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<VerificationFinding>();
        if (string.IsNullOrWhiteSpace(schemaPath) || !File.Exists(schemaPath))
        {
            findings.Add(Error("schema.file", "schema", null, $"Output schema blev ikke fundet: {schemaPath}"));
            return findings;
        }

        var schemaText = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        using var schemaDocument = JsonDocument.Parse(schemaText);
        var schemaRoot = schemaDocument.RootElement;
        var effectiveSchema = schemaRoot.TryGetProperty("schema", out var wrappedSchema) ? wrappedSchema : schemaRoot;
        ValidateElement(instance, effectiveSchema, effectiveSchema, "$", findings);
        return findings;
    }

    // Walk objects and arrays recursively so schema errors point to the exact failing path.
    private static void ValidateElement(JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<VerificationFinding> findings)
    {
        schema = ResolveSchema(schema, rootSchema);

        if (schema.TryGetProperty("type", out var typeElement) && !MatchesType(instance, typeElement))
        {
            findings.Add(Error("schema.type", path, null, $"JSON-værdien ved {path} har en ugyldig type i forhold til schemaet."));
            return;
        }

        if (schema.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array)
        {
            var rawValue = instance.GetRawText();
            var enumMatch = enumElement.EnumerateArray().Any(option => string.Equals(option.GetRawText(), rawValue, StringComparison.Ordinal));
            if (!enumMatch)
            {
                findings.Add(Error("schema.enum", path, null, $"JSON-værdien ved {path} er ikke en tilladt enum-værdi."));
            }
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(instance, schema, rootSchema, path, findings);
            return;
        }

        if (instance.ValueKind == JsonValueKind.Array)
        {
            ValidateArray(instance, schema, rootSchema, path, findings);
        }
    }

    private static void ValidateObject(JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<VerificationFinding> findings)
    {
        var properties = schema.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object
            ? propertiesElement
            : default;

        if (schema.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var requiredProperty in requiredElement.EnumerateArray().Select(item => item.GetString()).Where(name => !string.IsNullOrWhiteSpace(name)))
            {
                if (!instance.TryGetProperty(requiredProperty!, out _))
                {
                    findings.Add(Error("schema.required", $"{path}.{requiredProperty}", null, $"Required felt '{requiredProperty}' mangler i output."));
                }
            }
        }

        var additionalPropertiesAllowed = true;
        if (schema.TryGetProperty("additionalProperties", out var additionalPropertiesElement)
            && additionalPropertiesElement.ValueKind == JsonValueKind.False)
        {
            additionalPropertiesAllowed = false;
        }

        foreach (var property in instance.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                ValidateElement(property.Value, propertySchema, rootSchema, $"{path}.{property.Name}", findings);
            }
            else if (!additionalPropertiesAllowed)
            {
                findings.Add(Error("schema.additional_properties", $"{path}.{property.Name}", null, $"Feltet '{property.Name}' er ikke tilladt ifølge schemaet."));
            }
        }
    }

    private static void ValidateArray(JsonElement instance, JsonElement schema, JsonElement rootSchema, string path, List<VerificationFinding> findings)
    {
        if (!schema.TryGetProperty("items", out var itemsSchema))
        {
            return;
        }

        var index = 0;
        foreach (var item in instance.EnumerateArray())
        {
            ValidateElement(item, itemsSchema, rootSchema, $"{path}[{index}]", findings);
            index++;
        }
    }

    private static JsonElement ResolveSchema(JsonElement schema, JsonElement rootSchema)
    {
        if (!schema.TryGetProperty("$ref", out var refElement) || refElement.ValueKind != JsonValueKind.String)
        {
            return schema;
        }

        var reference = refElement.GetString();
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return schema;
        }

        JsonElement current = rootSchema;
        foreach (var segment in reference[2..].Split('/'))
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return schema;
            }
        }

        return current;
    }

    private static bool MatchesType(JsonElement instance, JsonElement typeElement)
    {
        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return MatchesType(instance, typeElement.GetString());
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var typeValue in typeElement.EnumerateArray())
            {
                if (typeValue.ValueKind == JsonValueKind.String && MatchesType(instance, typeValue.GetString()))
                {
                    return true;
                }
            }
        }

        return true;
    }

    private static bool MatchesType(JsonElement instance, string? typeName)
    {
        return typeName switch
        {
            "object" => instance.ValueKind == JsonValueKind.Object,
            "array" => instance.ValueKind == JsonValueKind.Array,
            "string" => instance.ValueKind == JsonValueKind.String,
            "number" => instance.ValueKind == JsonValueKind.Number,
            "integer" => instance.ValueKind == JsonValueKind.Number && IsInteger(instance),
            "boolean" => instance.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => instance.ValueKind == JsonValueKind.Null,
            _ => true
        };
    }

    private static bool IsInteger(JsonElement instance)
    {
        if (instance.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (instance.TryGetInt64(out _))
        {
            return true;
        }

        return decimal.TryParse(instance.GetRawText(), out var value) && decimal.Truncate(value) == value;
    }

    private static HashSet<string> ExtractIds(string? json, string arrayPropertyName, string idPropertyName)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json))
        {
            return ids;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty(arrayPropertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var item in array.EnumerateArray())
        {
            var id = GetString(item, idPropertyName);
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
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

    private static string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static List<CitationInfo> GetCitationObjects(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var citations) || citations.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return citations.EnumerateArray()
            .Select(citation => new CitationInfo(
                FileName: GetString(citation, "filename"),
                Excerpt: GetString(citation, "excerpt")))
            .ToList();
    }

    private static StageVerificationResult BuildResult(StageVerificationRequest request, IReadOnlyList<VerificationFinding> findings)
    {
        var errorCount = findings.Count(finding => string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase));
        var warningCount = findings.Count(finding => string.Equals(finding.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        var status = errorCount > 0 ? "fail" : warningCount > 0 ? "pass_with_warnings" : "pass";

        return new StageVerificationResult
        {
            Stage = ToStageName(request.Stage),
            DocumentId = request.DocumentId,
            Status = status,
            ApprovedForDownstream = errorCount == 0,
            WarningCount = warningCount,
            ErrorCount = errorCount,
            Findings = findings.ToList()
        };
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

    /// <summary>
    /// Creates a blocking finding used when downstream stages must not continue.
    /// </summary>
    private static VerificationFinding Error(string ruleId, string subjectType, string? subjectId, string messageDa)
        => new()
        {
            RuleId = ruleId,
            Severity = "error",
            SubjectType = subjectType,
            SubjectId = subjectId,
            MessageDa = messageDa,
            BlockingForDownstream = true
        };

    /// <summary>
    /// Creates a non-blocking finding used for quality signals and advisory behavior.
    /// </summary>
    private static VerificationFinding Warning(string ruleId, string subjectType, string? subjectId, string messageDa)
        => new()
        {
            RuleId = ruleId,
            Severity = "warning",
            SubjectType = subjectType,
            SubjectId = subjectId,
            MessageDa = messageDa,
            BlockingForDownstream = false
        };

    private sealed record CitationInfo(string FileName, string Excerpt);

    private sealed record ApplicationVisibleTextCandidate(string SubjectType, string? SubjectId, string FieldPath, string Text);
}