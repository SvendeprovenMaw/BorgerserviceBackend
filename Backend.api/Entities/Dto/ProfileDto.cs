using System.Collections.Generic;

namespace Backend.api.Entities.Dto;

/// <summary>
/// Editable personal profile fields used by the frontend profile screen.
/// </summary>
public class ProfileDto
{
    public string Name { get; set; } = string.Empty;

    public string ApplicantId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Municipality { get; set; } = string.Empty;

    public string ShortBio { get; set; } = string.Empty;

    public ProfileEnhancementDto ProfileEnhancement { get; set; } = new();
}

/// <summary>
/// Structured user-profile enhancement data that can be surfaced to the frontend and pipeline.
/// </summary>
public class ProfileEnhancementDto
{
    public string Headline { get; set; } = string.Empty;

    public string CurrentFocus { get; set; } = string.Empty;

    public List<string> PreferredRoles { get; set; } = [];

    public List<string> CoreCompetencies { get; set; } = [];

    public List<string> KeyStrengths { get; set; } = [];

    public List<string> NotableResults { get; set; } = [];

    public List<string> EducationHighlights { get; set; } = [];

    public List<string> LanguageHighlights { get; set; } = [];

    public string MobilityAndWorkPreferences { get; set; } = string.Empty;
}