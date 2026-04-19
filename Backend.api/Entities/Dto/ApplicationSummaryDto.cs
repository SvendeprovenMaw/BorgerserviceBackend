using System.ComponentModel.DataAnnotations;

namespace Backend.api.Entities.Dto;

/// <summary>
/// Read-only sent-application summary returned to the documents page.
/// </summary>
public class ApplicationSummaryDto
{
    public string Id { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;

    public string JobPosting { get; set; } = string.Empty;

    public string Status { get; set; } = "sent";

    public string CreatedAt { get; set; } = string.Empty;

    public string? SentAt { get; set; }

    public string Template { get; set; } = string.Empty;

    public string Draft { get; set; } = string.Empty;

    public string Final { get; set; } = string.Empty;
}

public sealed class FinishedApplicationRequestDto
{
    [Required]
    public string PipelineJobId { get; set; } = string.Empty;

    [Required]
    public ApplicationTemplateSnapshotDto TemplateSnapshot { get; set; } = new();

    [MinLength(1)]
    public IReadOnlyList<ApplicationSectionDto> Sections { get; set; } = [];
}

public sealed class ApplicationDetailDto : ApplicationSummaryDto
{
    public string ApplicantName { get; set; } = string.Empty;

    public string PipelineJobId { get; set; } = string.Empty;

    public string SubjectLine { get; set; } = string.Empty;

    public ApplicationTemplateSnapshotDto TemplateSnapshot { get; set; } = new();

    public IReadOnlyList<ApplicationSectionDto> Sections { get; set; } = [];

    public ApplicationCompanyContextDto CompanyContext { get; set; } = new();

    public IReadOnlyList<ApplicationArtifactDto> GeneratedArtifacts { get; set; } = [];
}

public sealed class ApplicationSectionDto
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Label { get; set; } = string.Empty;

    [Required]
    public string Text { get; set; } = string.Empty;

    public string? Kind { get; set; }
}

public sealed class ApplicationTemplateSnapshotDto
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? PreviewTone { get; set; }
}

public sealed class ApplicationCompanyContextDto
{
    public string SourceTypeLabel { get; set; } = string.Empty;

    public string WorkflowModeLabel { get; set; } = string.Empty;

    public string CandidateFileSummary { get; set; } = string.Empty;

    public string RequirementBreakdownLabel { get; set; } = string.Empty;

    public string CompanyOverrideLabel { get; set; } = string.Empty;

    public string ApplicantAddressHintLabel { get; set; } = string.Empty;

    public string ArtifactStatusLabel { get; set; } = string.Empty;

    public string PromptPathLabel { get; set; } = string.Empty;

    public string GeneratedAtLabel { get; set; } = string.Empty;

    public string JobStatusLabel { get; set; } = string.Empty;

    public string EmployeeCount { get; set; } = string.Empty;

    public string Industry { get; set; } = string.Empty;

    public string Trustpilot { get; set; } = string.Empty;

    public string Glassdoor { get; set; } = string.Empty;

    public IReadOnlyList<string> Values { get; set; } = [];

    public string Growth { get; set; } = string.Empty;

    public string SkillDevelopment { get; set; } = string.Empty;

    public string Commute { get; set; } = string.Empty;
}

public sealed class ApplicationArtifactDto
{
    public string Kind { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;
}