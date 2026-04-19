using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApplyAI.LlmPipeline;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Services.ApplyAIService;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services;

public interface ISentApplicationService
{
    Task<IReadOnlyList<ApplicationSummaryDto>> ListAsync(ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default);
    Task<ApplicationDetailDto?> GetAsync(ClaimsPrincipal claimsPrincipal, Guid applicationId, CancellationToken cancellationToken = default);
    Task<SaveFinishedApplicationResult> SaveAsync(ClaimsPrincipal claimsPrincipal, FinishedApplicationRequestDto request, CancellationToken cancellationToken = default);
}

public enum SaveFinishedApplicationError
{
    None,
    JobNotFound,
    MissingCompanyContext,
    MissingGeneratedApplication,
    InvalidSections,
}

public sealed record SaveFinishedApplicationResult(
    ApplicationDetailDto? Application,
    SaveFinishedApplicationError Error,
    string? Message)
{
    public static SaveFinishedApplicationResult Success(ApplicationDetailDto application) => new(application, SaveFinishedApplicationError.None, null);

    public static SaveFinishedApplicationResult Failure(SaveFinishedApplicationError error, string message) => new(null, error, message);
}

public sealed class SentApplicationService : ISentApplicationService
{
    private const string PipelineRoutePrefix = "/api/ai/pipeline/jobs";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplyAIDbContext _db;
    private readonly IUserService _userService;

    public SentApplicationService(ApplyAIDbContext db, IUserService userService)
    {
        _db = db;
        _userService = userService;
    }

    public async Task<IReadOnlyList<ApplicationSummaryDto>> ListAsync(ClaimsPrincipal claimsPrincipal, CancellationToken cancellationToken = default)
    {
        var user = await _userService.GetUser(claimsPrincipal);
        return await _db.SentApplications
            .AsNoTracking()
            .Where(application => application.UserId == user.Id)
            .OrderByDescending(application => application.SentAtUtc)
            .Select(application => MapSummary(application))
            .ToListAsync(cancellationToken);
    }

    public async Task<ApplicationDetailDto?> GetAsync(ClaimsPrincipal claimsPrincipal, Guid applicationId, CancellationToken cancellationToken = default)
    {
        var user = await _userService.GetUser(claimsPrincipal);
        var application = await _db.SentApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == applicationId && item.UserId == user.Id, cancellationToken);

        return application is null ? null : MapDetail(application);
    }

    public async Task<SaveFinishedApplicationResult> SaveAsync(ClaimsPrincipal claimsPrincipal, FinishedApplicationRequestDto request, CancellationToken cancellationToken = default)
    {
        var normalizedSections = NormalizeSections(request.Sections);
        if (normalizedSections.Count == 0)
        {
            return SaveFinishedApplicationResult.Failure(
                SaveFinishedApplicationError.InvalidSections,
                "At least one non-empty application section must be provided when finishing the application.");
        }

        var user = await _userService.GetUser(claimsPrincipal);
        var existing = await _db.SentApplications
            .AsNoTracking()
            .SingleOrDefaultAsync(
                application => application.UserId == user.Id && application.PipelineJobId == request.PipelineJobId,
                cancellationToken);

        if (existing is not null)
        {
            return SaveFinishedApplicationResult.Success(MapDetail(existing));
        }

        var job = await _db.ApplyAiPipelineJobs
            .Include(item => item.PhaseStates)
            .Include(item => item.Artifacts)
            .SingleOrDefaultAsync(item => item.Id == request.PipelineJobId && item.UserId == user.Id, cancellationToken);

        if (job is null)
        {
            return SaveFinishedApplicationResult.Failure(
                SaveFinishedApplicationError.JobNotFound,
                $"Pipeline job '{request.PipelineJobId}' was not found for the current user.");
        }

        var companyContextState = job.PhaseStates.SingleOrDefault(item => item.Phase == PipelinePhase.CompanyContext && !string.IsNullOrWhiteSpace(item.DocumentJson));
        if (companyContextState is null)
        {
            return SaveFinishedApplicationResult.Failure(
                SaveFinishedApplicationError.MissingCompanyContext,
                "The finished application could not be saved because the company-context phase document is missing.");
        }

        var applicationState = job.PhaseStates.SingleOrDefault(item => item.Phase == PipelinePhase.ApplicationGeneration && !string.IsNullOrWhiteSpace(item.DocumentJson));
        if (applicationState is null)
        {
            return SaveFinishedApplicationResult.Failure(
                SaveFinishedApplicationError.MissingGeneratedApplication,
                "The finished application could not be saved because the generated application document is missing.");
        }

        var requirementsState = job.PhaseStates.SingleOrDefault(item => item.Phase == PipelinePhase.Requirements && !string.IsNullOrWhiteSpace(item.DocumentJson));
        var companyContextDocument = ParseDocument(companyContextState.DocumentJson!);
        var applicationDocument = ParseDocument(applicationState.DocumentJson!);
        var requirementsDocument = requirementsState is null ? new JsonObject() : ParseDocument(requirementsState.DocumentJson!);

        var templateSnapshot = NormalizeTemplate(request.TemplateSnapshot);
        var applicantName = DeriveApplicantName(applicationDocument, user);
        var companyName = DeriveCompanyName(companyContextDocument, applicationDocument, job);
        var positionTitle = DerivePositionTitle(applicationDocument, job);
        var subjectLine = DeriveSubjectLine(applicationDocument, normalizedSections, positionTitle);
        var finalText = BuildFinalText(normalizedSections);
        var companyContext = BuildCompanyContext(job, companyContextDocument, requirementsDocument);
        var generatedArtifacts = BuildGeneratedArtifacts(job.Artifacts);
        var sentAtUtc = DateTimeOffset.UtcNow;

        var application = new SentApplication
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PipelineJobId = job.Id,
            Company = companyName,
            Position = positionTitle,
            ContactEmail = string.Empty,
            JobPosting = DeriveJobPostingLabel(companyContextDocument),
            Status = "sent",
            CreatedAtUtc = job.CreatedAtUtc,
            SentAtUtc = sentAtUtc,
            ApplicantName = applicantName,
            SubjectLine = subjectLine,
            FinalText = finalText,
            TemplateSnapshotJson = JsonSerializer.Serialize(templateSnapshot, JsonOptions),
            SectionsJson = JsonSerializer.Serialize(normalizedSections, JsonOptions),
            CompanyContextJson = JsonSerializer.Serialize(companyContext, JsonOptions),
            GeneratedArtifactsJson = JsonSerializer.Serialize(generatedArtifacts, JsonOptions),
        };

        _db.SentApplications.Add(application);
        await _db.SaveChangesAsync(cancellationToken);
        return SaveFinishedApplicationResult.Success(MapDetail(application));
    }

    private static IReadOnlyList<ApplicationSectionDto> NormalizeSections(IReadOnlyList<ApplicationSectionDto> sections)
    {
        return sections
            .Select(section => new ApplicationSectionDto
            {
                Id = section.Id.Trim(),
                Label = section.Label.Trim(),
                Text = section.Text.Trim(),
                Kind = string.IsNullOrWhiteSpace(section.Kind) ? null : section.Kind.Trim(),
            })
            .Where(section => !string.IsNullOrWhiteSpace(section.Id)
                && !string.IsNullOrWhiteSpace(section.Label)
                && !string.IsNullOrWhiteSpace(section.Text))
            .ToArray();
    }

    private static ApplicationTemplateSnapshotDto NormalizeTemplate(ApplicationTemplateSnapshotDto templateSnapshot)
    {
        return new ApplicationTemplateSnapshotDto
        {
            Id = templateSnapshot.Id.Trim(),
            Name = templateSnapshot.Name.Trim(),
            Version = string.IsNullOrWhiteSpace(templateSnapshot.Version) ? null : templateSnapshot.Version.Trim(),
            PreviewTone = string.IsNullOrWhiteSpace(templateSnapshot.PreviewTone) ? null : templateSnapshot.PreviewTone.Trim(),
        };
    }

    private static ApplicationSummaryDto MapSummary(SentApplication application)
    {
        return new ApplicationSummaryDto
        {
            Id = application.Id.ToString(),
            Company = application.Company,
            Position = application.Position,
            ContactEmail = application.ContactEmail,
            JobPosting = application.JobPosting,
            Status = application.Status,
            CreatedAt = application.CreatedAtUtc.ToString("O"),
            SentAt = application.SentAtUtc.ToString("O"),
            Template = Deserialize(application.TemplateSnapshotJson, new ApplicationTemplateSnapshotDto()).Name,
            Draft = application.SubjectLine,
            Final = application.FinalText,
        };
    }

    private static ApplicationDetailDto MapDetail(SentApplication application)
    {
        var summary = MapSummary(application);
        return new ApplicationDetailDto
        {
            Id = summary.Id,
            Company = summary.Company,
            Position = summary.Position,
            ContactEmail = summary.ContactEmail,
            JobPosting = summary.JobPosting,
            Status = summary.Status,
            CreatedAt = summary.CreatedAt,
            SentAt = summary.SentAt,
            Template = summary.Template,
            Draft = summary.Draft,
            Final = summary.Final,
            ApplicantName = application.ApplicantName,
            PipelineJobId = application.PipelineJobId.ToString(),
            SubjectLine = application.SubjectLine,
            TemplateSnapshot = Deserialize(application.TemplateSnapshotJson, new ApplicationTemplateSnapshotDto()),
            Sections = Deserialize(application.SectionsJson, Array.Empty<ApplicationSectionDto>()),
            CompanyContext = Deserialize(application.CompanyContextJson, new ApplicationCompanyContextDto()),
            GeneratedArtifacts = Deserialize(application.GeneratedArtifactsJson, Array.Empty<ApplicationArtifactDto>()),
        };
    }

    private static T Deserialize<T>(string json, T fallback)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static JsonObject ParseDocument(string json)
    {
        try
        {
            return JsonNode.Parse(json)?.AsObject() ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static ApplicationCompanyContextDto BuildCompanyContext(
        ApplyAiPipelineJob job,
        JsonObject companyContextDocument,
        JsonObject requirementsDocument)
    {
        var companyContextOutput = AsObject(companyContextDocument["phaseOutput"]);
        var meta = AsObject(companyContextOutput["_meta"]);
        var companyProfile = AsObject(companyContextOutput["company_profile"]);
        var employeeCount = AsObject(companyProfile["employee_count"]);
        var trustpilot = AsObject(companyProfile["trustpilot"]);
        var glassdoor = AsObject(companyProfile["glassdoor"]);
        var economics = AsObject(companyContextOutput["economics"]);
        var employeeConditions = AsObject(companyContextOutput["employee_conditions"]);
        var distance = AsObject(companyContextOutput["distance"]);
        var mediaMentions = AsObject(companyContextOutput["media_mentions"]);
        var jobPostingSource = AsObject(companyContextDocument["jobPostingSource"]);
        var companyContextOverrides = AsObject(companyContextDocument["companyContextOverrides"]);
        var storedArtifact = AsObject(companyContextDocument["storedJobPostingArtifact"]);
        var assetPaths = AsObject(companyContextDocument["llmAssetPaths"]);
        var candidateFiles = ReadStringArray(companyContextDocument["candidateFiles"]);
        var requirements = AsArray(AsObject(requirementsDocument["phaseOutput"])["requirements"]);
        var values = ReadStringArray(companyProfile["homepage_values_mission_vision_da"]);
        if (values.Length == 0)
        {
            values = ReadStringArray(mediaMentions["recent_media_mentions_da"]).Take(3).ToArray();
        }

        return new ApplicationCompanyContextDto
        {
            SourceTypeLabel = DescribeSourceType(ReadString(jobPostingSource["sourceType"])),
            WorkflowModeLabel = $"{job.WorkflowMode} workflow",
            CandidateFileSummary = FormatCandidateFileSummary(candidateFiles),
            RequirementBreakdownLabel = DescribeRequirementBreakdown(requirements),
            CompanyOverrideLabel = !string.IsNullOrWhiteSpace(ReadString(companyContextOverrides["companyName"]))
                ? $"Company override: {ReadString(companyContextOverrides["companyName"])}"
                : "No company override supplied.",
            ApplicantAddressHintLabel = !string.IsNullOrWhiteSpace(ReadString(companyContextOverrides["applicantAddressHint"]))
                ? $"Address hint: {ReadString(companyContextOverrides["applicantAddressHint"])}"
                : "No applicant address hint supplied.",
            ArtifactStatusLabel = !string.IsNullOrWhiteSpace(ReadString(storedArtifact["displayName"]))
                ? $"Stored job posting artifact ready: {ReadString(storedArtifact["displayName"])}"
                : "Stored job posting artifact not available.",
            PromptPathLabel = !string.IsNullOrWhiteSpace(ReadString(assetPaths["promptPath"]))
                ? $"Prompt asset: {ReadString(assetPaths["promptPath"])}"
                : "Prompt asset unavailable.",
            GeneratedAtLabel = FormatTimestamp(ReadString(companyContextDocument["generatedAtUtc"])),
            JobStatusLabel = job.StatusMessage,
            EmployeeCount = ReadString(employeeCount["display_da"]) ?? "Ukendt",
            Industry = ReadString(companyProfile["industry_da"]) ?? "Ukendt",
            Trustpilot = ReadString(trustpilot["display_da"]) ?? "Ukendt",
            Glassdoor = ReadString(glassdoor["display_da"]) ?? "Ukendt",
            Values = values,
            Growth = ReadString(economics["growth_assessment_da"]) ?? "Ingen vurdering tilgængelig.",
            SkillDevelopment = ReadString(employeeConditions["skill_development_opportunities_da"]) ?? "Ingen udviklingsvurdering tilgængelig.",
            Commute = ReadString(distance["calculation_basis_da"])
                ?? ReadString(meta["input_summary_da"])
                ?? "Ingen pendlingsvurdering tilgængelig.",
        };
    }

    private static IReadOnlyList<ApplicationArtifactDto> BuildGeneratedArtifacts(IEnumerable<ApplyAiPipelineArtifact> artifacts)
    {
        return artifacts
            .Where(artifact => artifact.Phase == PipelinePhase.ApplicationGeneration)
            .Where(artifact => artifact.ArtifactKind is PipelineArtifactKind.HtmlDocument
                or PipelineArtifactKind.CssStylesheet
                or PipelineArtifactKind.PdfDocument
                or PipelineArtifactKind.Advisory
                or PipelineArtifactKind.Other)
            .OrderBy(artifact => artifact.ArtifactKind.ToString())
            .ThenBy(artifact => artifact.DisplayName)
            .Select(artifact => new ApplicationArtifactDto
            {
                Kind = artifact.ArtifactKind.ToString(),
                DisplayName = artifact.DisplayName,
                RelativePath = BuildArtifactContentUrl(artifact.JobId, artifact.Id),
                MediaType = artifact.MediaType,
            })
            .ToArray();
    }

    private static string DeriveApplicantName(JsonObject applicationDocument, User user)
    {
        var meta = AsObject(AsObject(applicationDocument["phaseOutput"])["_meta"]);
        return ReadString(meta["applicant_display_name"]) ?? user.Username;
    }

    private static string DeriveCompanyName(JsonObject companyContextDocument, JsonObject applicationDocument, ApplyAiPipelineJob job)
    {
        var applicationMeta = AsObject(AsObject(applicationDocument["phaseOutput"])["_meta"]);
        var companyContextMeta = AsObject(AsObject(companyContextDocument["phaseOutput"])["_meta"]);
        var companyContextOverrides = AsObject(companyContextDocument["companyContextOverrides"]);
        var companyName = ReadString(applicationMeta["company_name"])
            ?? ReadString(companyContextMeta["company_name"])
            ?? ReadString(companyContextOverrides["companyName"]);

        if (!string.IsNullOrWhiteSpace(companyName))
        {
            return companyName;
        }

        if (!string.IsNullOrWhiteSpace(job.CompanyNameOverride))
        {
            return job.CompanyNameOverride;
        }

        return job.DisplayRunName;
    }

    private static string DerivePositionTitle(JsonObject applicationDocument, ApplyAiPipelineJob job)
    {
        var meta = AsObject(AsObject(applicationDocument["phaseOutput"])["_meta"]);
        return ReadString(meta["position_title"])
            ?? ReadString(meta["job_posting_title"])
            ?? job.DisplayRunName;
    }

    private static string DeriveSubjectLine(
        JsonObject applicationDocument,
        IReadOnlyList<ApplicationSectionDto> sections,
        string positionTitle)
    {
        var subjectSection = sections.FirstOrDefault(section => string.Equals(section.Id, "subject_line", StringComparison.OrdinalIgnoreCase));
        if (subjectSection is not null)
        {
            return subjectSection.Text;
        }

        var strategy = AsObject(AsObject(applicationDocument["phaseOutput"])["application_strategy"]);
        return ReadString(strategy["subject_line_da"]) ?? $"Ansøgning til stillingen som {positionTitle}";
    }

    private static string BuildFinalText(IReadOnlyList<ApplicationSectionDto> sections)
    {
        return string.Join(
            "\n\n",
            sections
                .Where(section => !string.Equals(section.Id, "subject_line", StringComparison.OrdinalIgnoreCase))
                .Select(section => section.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static string DeriveJobPostingLabel(JsonObject companyContextDocument)
    {
        var meta = AsObject(AsObject(companyContextDocument["phaseOutput"])["_meta"]);
        var parsedFiles = ReadStringArray(meta["parsed_files"]);
        if (parsedFiles.Length > 0)
        {
            return parsedFiles[0];
        }

        var jobPostingSource = AsObject(companyContextDocument["jobPostingSource"]);
        return ReadString(jobPostingSource["fileName"])
            ?? ReadString(jobPostingSource["reference"])
            ?? "Ukendt jobopslag";
    }

    private static JsonObject AsObject(JsonNode? node)
    {
        return node as JsonObject ?? new JsonObject();
    }

    private static JsonArray AsArray(JsonNode? node)
    {
        return node as JsonArray ?? new JsonArray();
    }

    private static string? ReadString(JsonNode? node)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
        {
            return stringValue.Trim();
        }

        return null;
    }

    private static string[] ReadStringArray(JsonNode? node)
    {
        return AsArray(node)
            .Select(ReadString)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();
    }

    private static string BuildArtifactContentUrl(Guid jobId, Guid artifactId)
    {
        return $"{PipelineRoutePrefix}/{jobId:N}/artifacts/{artifactId:N}/content";
    }

    private static string DescribeSourceType(string? sourceType)
    {
        return sourceType switch
        {
            "RemoteUrl" => "Rendered from link",
            "UploadedFile" => "Uploaded PDF",
            _ => "Pipeline intake source",
        };
    }

    private static string FormatCandidateFileSummary(IReadOnlyList<string> candidateFiles)
    {
        if (candidateFiles.Count == 0)
        {
            return "No candidate files were attached to this run.";
        }

        var suffix = candidateFiles.Count == 1 ? string.Empty : "s";
        return $"{candidateFiles.Count} candidate file{suffix} attached to the run.";
    }

    private static string DescribeRequirementBreakdown(JsonArray requirements)
    {
        var counts = new
        {
            MustHave = requirements.Count(item => string.Equals(ReadString(AsObject(item)["importance"]), "must_have", StringComparison.OrdinalIgnoreCase)),
            ShouldHave = requirements.Count(item => string.Equals(ReadString(AsObject(item)["importance"]), "should_have", StringComparison.OrdinalIgnoreCase)),
            NiceToHave = requirements.Count(item => string.Equals(ReadString(AsObject(item)["importance"]), "nice_to_have", StringComparison.OrdinalIgnoreCase)),
        };

        return $"{counts.MustHave} must-have / {counts.ShouldHave} should-have / {counts.NiceToHave} nice-to-have";
    }

    private static string FormatTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Generation timestamp unavailable.";
        }

        if (!DateTimeOffset.TryParse(value, out var timestamp))
        {
            return $"Generated at {value}.";
        }

        return $"Generated {timestamp:dd MMM yyyy HH:mm}.";
    }
}