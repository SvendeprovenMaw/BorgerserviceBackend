using System.Text.Json;
using System.Text.Json.Serialization;
using Backend.api.Entities.Dto;

namespace Backend.api.Services;

public static class ProfileDefaults
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    public static ProfileEnhancementDto CreateDefaultEnhancement() => new()
    {
        Headline = "Alsidig ansøger med fokus på stabile leverancer og tydelig kommunikation",
        CurrentFocus = "Roller hvor dokumenteret erfaring, samarbejde og et troværdigt match til opgaverne er vigtigere end generiske buzzwords.",
        PreferredRoles =
        [
            "Sagsbehandler",
            "Koordinator",
            "Udvikler",
        ],
        CoreCompetencies =
        [
            "Struktureret opgaveløsning",
            "Dokumentation og kvalitet",
            "Samarbejde på tværs",
        ],
        KeyStrengths =
        [
            "Troværdig og konkret kommunikation",
            "Hurtig indlæring af domæne og arbejdsgange",
            "Stabil opfølgning på aftaler og detaljer",
        ],
        NotableResults =
        [
            "Har leveret driftssikre opgaver i både team- og selvstændige forløb",
            "Har dokumenteret løsninger, så andre kan overtage og videreudvikle arbejdet",
        ],
        EducationHighlights =
        [
            "Anvender både faglig læring og praksisnære erfaringer i ansøgningerne",
        ],
        LanguageHighlights =
        [
            "Dansk - professionel skrift og tale",
            "Engelsk - arbejdsniveau",
        ],
        MobilityAndWorkPreferences = "Åben for både fysisk fremmøde, hybridarbejde og pendling, når det faglige match er tydeligt.",
    };

    public static ProfilePreferencesDto CreateDefaultPreferences(string applicantId, string applicantDisplayName) => new()
    {
        ApplicantId = applicantId,
        ApplicantDisplayName = applicantDisplayName,
        TargetLanguage = "da",
        Tone = "warm_professional",
        LengthTarget = "medium",
        FormalityLevel = "medium",
        MotivationStyle = new MotivationStylePreferencesDto
        {
            AllowGeneralInterestLanguage = true,
            AllowGroundedMotivationClaims = true,
            AllowPassionLanguage = false,
            AllowCompanyPraise = false,
        },
        SelfPresentationStyle = new SelfPresentationStylePreferencesDto
        {
            ConfidenceLevel = "medium",
            PreferModestWording = true,
            AllowResultForwardLanguage = true,
            AllowLeadershipEmphasis = false,
        },
        EmphasisPreferences = new EmphasisPreferencesDto
        {
            PrioritizeRequirementsWithStrongMatch = true,
            PrioritizePracticalExperience = true,
            PrioritizePersonalTraits = true,
            PrioritizeFormalEducation = false,
            PrioritizeCertifications = false,
            PrioritizeCollaboration = true,
            PrioritizeStabilityAndReliability = true,
            PreferredTopStrengthCount = 3,
        },
        ContentConstraints = new ContentConstraintsPreferencesDto
        {
            AvoidUnverifiedSuperlatives = true,
            AvoidRepeatingCvFactsVerbatim = true,
            AvoidMentioningMissingRequirements = true,
            AvoidSalaryDiscussion = true,
            AvoidPrivateLifeDetails = true,
            MaxMainContentCharacters = 1550,
            DisallowedPhrasesDa = [],
        },
        StructuralPreferences = new StructuralPreferencesDto
        {
            IncludeExplicitOpeningReferenceToPosition = true,
            IncludeExplicitMatchParagraph = true,
            IncludeExplicitMotivationParagraph = true,
            IncludeShortFutureContributionParagraph = true,
            PreferShortParagraphs = true,
        },
        ClosingPreferences = new ClosingPreferencesDto
        {
            ClosingStyle = "warm",
            IncludeInterviewInterest = true,
            IncludeThanksForConsideration = true,
        },
        FitStrategy = new FitStrategyPreferencesDto
        {
            GuidanceMode = "optimistic",
            IncludeFitAdvisory = true,
            AllowApplicationOnWeakMatch = true,
            PreferTransferableStrengthsWhenDirectMatchIsWeak = true,
            AllowStretchPositioning = true,
        },
    };

    public static ProfileEnhancementDto DeserializeProfileEnhancement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateDefaultEnhancement();
        }

        var enhancement = JsonSerializer.Deserialize<ProfileEnhancementDto>(json, SerializerOptions);
        if (HasMeaningfulEnhancement(enhancement))
        {
            return enhancement!;
        }

        var seededEnhancement = JsonSerializer.Deserialize<SeededProfileEnhancementDto>(json, SerializerOptions);
        enhancement = seededEnhancement?.ToProfileEnhancementDto();
        return HasMeaningfulEnhancement(enhancement) ? enhancement! : CreateDefaultEnhancement();
    }

    public static ProfilePreferencesDto DeserializePreferences(string? json, string applicantId, string applicantDisplayName)
    {
        var preferences = string.IsNullOrWhiteSpace(json)
            ? CreateDefaultPreferences(applicantId, applicantDisplayName)
            : JsonSerializer.Deserialize<ProfilePreferencesDto>(json, SerializerOptions) ?? CreateDefaultPreferences(applicantId, applicantDisplayName);

        return NormalizePreferences(preferences, applicantId, applicantDisplayName);
    }

    public static ProfilePreferencesDto NormalizePreferences(ProfilePreferencesDto preferences, string applicantId, string applicantDisplayName)
    {
        preferences.ApplicantId = string.IsNullOrWhiteSpace(preferences.ApplicantId)
            ? applicantId
            : preferences.ApplicantId;
        preferences.ApplicantDisplayName = string.IsNullOrWhiteSpace(preferences.ApplicantDisplayName)
            ? applicantDisplayName
            : preferences.ApplicantDisplayName;
        preferences.MotivationStyle ??= new MotivationStylePreferencesDto();
        preferences.SelfPresentationStyle ??= new SelfPresentationStylePreferencesDto();
        preferences.EmphasisPreferences ??= new EmphasisPreferencesDto();
        preferences.ContentConstraints ??= new ContentConstraintsPreferencesDto();
        preferences.StructuralPreferences ??= new StructuralPreferencesDto();
        preferences.ClosingPreferences ??= new ClosingPreferencesDto();
        preferences.FitStrategy ??= CreateDefaultPreferences(applicantId, applicantDisplayName).FitStrategy;
        preferences.ContentConstraints.DisallowedPhrasesDa ??= [];
        return preferences;
    }

    public static string SerializeProfileEnhancement(ProfileEnhancementDto enhancement)
        => JsonSerializer.Serialize(enhancement, SerializerOptions);

    public static string SerializePreferences(ProfilePreferencesDto preferences)
        => JsonSerializer.Serialize(preferences, SerializerOptions);

    private static bool HasMeaningfulEnhancement(ProfileEnhancementDto? enhancement)
    {
        return enhancement is not null
            && (
                !string.IsNullOrWhiteSpace(enhancement.Headline)
                || !string.IsNullOrWhiteSpace(enhancement.CurrentFocus)
                || enhancement.PreferredRoles.Count > 0
                || enhancement.CoreCompetencies.Count > 0
                || enhancement.KeyStrengths.Count > 0
                || enhancement.NotableResults.Count > 0
                || enhancement.EducationHighlights.Count > 0
                || enhancement.LanguageHighlights.Count > 0
                || !string.IsNullOrWhiteSpace(enhancement.MobilityAndWorkPreferences));
    }

    private sealed class SeededProfileEnhancementDto
    {
        [JsonPropertyName("headline")]
        public string Headline { get; set; } = string.Empty;

        [JsonPropertyName("current_focus")]
        public string CurrentFocus { get; set; } = string.Empty;

        [JsonPropertyName("preferred_roles")]
        public List<string> PreferredRoles { get; set; } = [];

        [JsonPropertyName("core_competencies")]
        public List<string> CoreCompetencies { get; set; } = [];

        [JsonPropertyName("key_strengths")]
        public List<string> KeyStrengths { get; set; } = [];

        [JsonPropertyName("notable_results")]
        public List<string> NotableResults { get; set; } = [];

        [JsonPropertyName("education_highlights")]
        public List<string> EducationHighlights { get; set; } = [];

        [JsonPropertyName("language_highlights")]
        public List<string> LanguageHighlights { get; set; } = [];

        [JsonPropertyName("mobility_and_work_preferences")]
        public string MobilityAndWorkPreferences { get; set; } = string.Empty;

        public ProfileEnhancementDto ToProfileEnhancementDto()
            => new()
            {
                Headline = Headline,
                CurrentFocus = CurrentFocus,
                PreferredRoles = PreferredRoles,
                CoreCompetencies = CoreCompetencies,
                KeyStrengths = KeyStrengths,
                NotableResults = NotableResults,
                EducationHighlights = EducationHighlights,
                LanguageHighlights = LanguageHighlights,
                MobilityAndWorkPreferences = MobilityAndWorkPreferences,
            };
    }
}