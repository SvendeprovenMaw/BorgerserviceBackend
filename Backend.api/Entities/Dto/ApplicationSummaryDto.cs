namespace Backend.api.Entities.Dto;

/// <summary>
/// Read-only sent-application summary returned to the documents page.
/// </summary>
public sealed class ApplicationSummaryDto
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