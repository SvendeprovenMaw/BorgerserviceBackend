using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Backend.api.Entities.Dto;

/// <summary>
/// Preferences payload aligned with the application-generation preference schema.
/// </summary>
public class ProfilePreferencesDto
{
    [JsonPropertyName("applicant_display_name")]
    public string ApplicantDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("applicant_id")]
    public string ApplicantId { get; set; } = string.Empty;

    [JsonPropertyName("target_language")]
    public string TargetLanguage { get; set; } = "da";

    [JsonPropertyName("tone")]
    public string Tone { get; set; } = "warm_professional";

    [JsonPropertyName("length_target")]
    public string LengthTarget { get; set; } = "medium";

    [JsonPropertyName("formality_level")]
    public string FormalityLevel { get; set; } = "medium";

    [JsonPropertyName("motivation_style")]
    public MotivationStylePreferencesDto MotivationStyle { get; set; } = new();

    [JsonPropertyName("self_presentation_style")]
    public SelfPresentationStylePreferencesDto SelfPresentationStyle { get; set; } = new();

    [JsonPropertyName("emphasis_preferences")]
    public EmphasisPreferencesDto EmphasisPreferences { get; set; } = new();

    [JsonPropertyName("content_constraints")]
    public ContentConstraintsPreferencesDto ContentConstraints { get; set; } = new();

    [JsonPropertyName("structural_preferences")]
    public StructuralPreferencesDto StructuralPreferences { get; set; } = new();

    [JsonPropertyName("closing_preferences")]
    public ClosingPreferencesDto ClosingPreferences { get; set; } = new();

    [JsonPropertyName("fit_strategy")]
    public FitStrategyPreferencesDto FitStrategy { get; set; } = new();
}

public class MotivationStylePreferencesDto
{
    [JsonPropertyName("allow_general_interest_language")]
    public bool AllowGeneralInterestLanguage { get; set; } = true;

    [JsonPropertyName("allow_grounded_motivation_claims")]
    public bool AllowGroundedMotivationClaims { get; set; } = true;

    [JsonPropertyName("allow_passion_language")]
    public bool AllowPassionLanguage { get; set; }

    [JsonPropertyName("allow_company_praise")]
    public bool AllowCompanyPraise { get; set; }
}

public class SelfPresentationStylePreferencesDto
{
    [JsonPropertyName("confidence_level")]
    public string ConfidenceLevel { get; set; } = "medium";

    [JsonPropertyName("prefer_modest_wording")]
    public bool PreferModestWording { get; set; } = true;

    [JsonPropertyName("allow_result_forward_language")]
    public bool AllowResultForwardLanguage { get; set; } = true;

    [JsonPropertyName("allow_leadership_emphasis")]
    public bool AllowLeadershipEmphasis { get; set; }
}

public class EmphasisPreferencesDto
{
    [JsonPropertyName("prioritize_requirements_with_strong_match")]
    public bool PrioritizeRequirementsWithStrongMatch { get; set; } = true;

    [JsonPropertyName("prioritize_practical_experience")]
    public bool PrioritizePracticalExperience { get; set; } = true;

    [JsonPropertyName("prioritize_personal_traits")]
    public bool PrioritizePersonalTraits { get; set; } = true;

    [JsonPropertyName("prioritize_formal_education")]
    public bool PrioritizeFormalEducation { get; set; }

    [JsonPropertyName("prioritize_certifications")]
    public bool PrioritizeCertifications { get; set; }

    [JsonPropertyName("prioritize_collaboration")]
    public bool PrioritizeCollaboration { get; set; } = true;

    [JsonPropertyName("prioritize_stability_and_reliability")]
    public bool PrioritizeStabilityAndReliability { get; set; } = true;

    [JsonPropertyName("preferred_top_strength_count")]
    public int PreferredTopStrengthCount { get; set; } = 3;
}

public class ContentConstraintsPreferencesDto
{
    [JsonPropertyName("avoid_unverified_superlatives")]
    public bool AvoidUnverifiedSuperlatives { get; set; } = true;

    [JsonPropertyName("avoid_repeating_cv_facts_verbatim")]
    public bool AvoidRepeatingCvFactsVerbatim { get; set; } = true;

    [JsonPropertyName("avoid_mentioning_missing_requirements")]
    public bool AvoidMentioningMissingRequirements { get; set; } = true;

    [JsonPropertyName("avoid_salary_discussion")]
    public bool AvoidSalaryDiscussion { get; set; } = true;

    [JsonPropertyName("avoid_private_life_details")]
    public bool AvoidPrivateLifeDetails { get; set; } = true;

    [JsonPropertyName("max_main_content_characters")]
    public int MaxMainContentCharacters { get; set; } = 1550;

    [JsonPropertyName("disallowed_phrases_da")]
    public List<string> DisallowedPhrasesDa { get; set; } = [];
}

public class StructuralPreferencesDto
{
    [JsonPropertyName("include_explicit_opening_reference_to_position")]
    public bool IncludeExplicitOpeningReferenceToPosition { get; set; } = true;

    [JsonPropertyName("include_explicit_match_paragraph")]
    public bool IncludeExplicitMatchParagraph { get; set; } = true;

    [JsonPropertyName("include_explicit_motivation_paragraph")]
    public bool IncludeExplicitMotivationParagraph { get; set; } = true;

    [JsonPropertyName("include_short_future_contribution_paragraph")]
    public bool IncludeShortFutureContributionParagraph { get; set; } = true;

    [JsonPropertyName("prefer_short_paragraphs")]
    public bool PreferShortParagraphs { get; set; } = true;
}

public class ClosingPreferencesDto
{
    [JsonPropertyName("closing_style")]
    public string ClosingStyle { get; set; } = "warm";

    [JsonPropertyName("include_interview_interest")]
    public bool IncludeInterviewInterest { get; set; } = true;

    [JsonPropertyName("include_thanks_for_consideration")]
    public bool IncludeThanksForConsideration { get; set; } = true;
}

public class FitStrategyPreferencesDto
{
    [JsonPropertyName("guidance_mode")]
    public string GuidanceMode { get; set; } = "optimistic";

    [JsonPropertyName("include_fit_advisory")]
    public bool IncludeFitAdvisory { get; set; } = true;

    [JsonPropertyName("allow_application_on_weak_match")]
    public bool AllowApplicationOnWeakMatch { get; set; } = true;

    [JsonPropertyName("prefer_transferable_strengths_when_direct_match_is_weak")]
    public bool PreferTransferableStrengthsWhenDirectMatchIsWeak { get; set; } = true;

    [JsonPropertyName("allow_stretch_positioning")]
    public bool AllowStretchPositioning { get; set; } = true;
}