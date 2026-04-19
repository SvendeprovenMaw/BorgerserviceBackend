namespace Backend.api.Entities;

public sealed class SentApplication
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public Guid PipelineJobId { get; set; }

    public string Company { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;

    public string JobPosting { get; set; } = string.Empty;

    public string Status { get; set; } = "sent";

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset SentAtUtc { get; set; }

    public string ApplicantName { get; set; } = string.Empty;

    public string SubjectLine { get; set; } = string.Empty;

    public string FinalText { get; set; } = string.Empty;

    public string TemplateSnapshotJson { get; set; } = "{}";

    public string SectionsJson { get; set; } = "[]";

    public string CompanyContextJson { get; set; } = "{}";

    public string GeneratedArtifactsJson { get; set; } = "[]";
}