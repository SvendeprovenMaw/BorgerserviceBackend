using ApplyAI.LlmPipeline;
using Backend.api.Enums;
using System.Text.RegularExpressions;

namespace Backend.api.Services;

public enum CareerDocumentStorageArea
{
    Cv,
    Bio,
    SuccessStories,
}

public static class StoragePathBuilder
{
    private static readonly Regex InvalidPathCharacters = new("[^A-Za-z0-9._-]", RegexOptions.Compiled);

    public static bool UsesCareerDocumentLayout(FileCategory fileCategory)
    {
        return fileCategory is FileCategory.Cv or FileCategory.Bio or FileCategory.CarreerDocument;
    }

    public static CareerDocumentStorageArea MapCareerDocumentArea(FileCategory fileCategory)
    {
        return fileCategory switch
        {
            FileCategory.Cv => CareerDocumentStorageArea.Cv,
            FileCategory.Bio => CareerDocumentStorageArea.Bio,
            FileCategory.CarreerDocument => CareerDocumentStorageArea.SuccessStories,
            _ => throw new ArgumentOutOfRangeException(nameof(fileCategory), fileCategory, "The requested file category does not use the CarreerDocuments layout."),
        };
    }

    public static string BuildCareerDocumentPrefix(Guid userId, CareerDocumentStorageArea area)
    {
        return $"users/{userId}/CarreerDocuments/{area}/";
    }

    public static string BuildUserDocumentPrefix(Guid userId, FileCategory fileCategory)
    {
        return BuildCareerDocumentPrefix(userId, MapCareerDocumentArea(fileCategory));
    }

    public static string BuildUserDocumentStorageKey(Guid userId, FileCategory fileCategory, string fileName, Guid? storageObjectId = null)
    {
        var prefix = BuildUserDocumentPrefix(userId, fileCategory).TrimEnd('/');
        var objectId = storageObjectId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var safeFileName = BuildSafeFileName(fileName, "document.bin");
        return $"{prefix}/{objectId}_{safeFileName}";
    }

    public static string BuildRunStoragePrefix(Guid userId, DateTimeOffset createdAtUtc, Guid jobId)
    {
        return $"users/{userId}/Runs/{createdAtUtc:yyyy-MM-dd}/{jobId:N}";
    }

    public static string BuildRunArtifactStorageKey(string runStoragePrefix, string relativePath)
    {
        return $"{TrimSlashes(runStoragePrefix)}/{TrimSlashes(relativePath)}";
    }

    public static string BuildStoredJobPostingRelativePath(Guid artifactId, string fileName)
    {
        return $"inputs/job_listing/{artifactId:N}__{BuildSafeFileName(fileName, "job-posting.pdf")}";
    }

    public static string BuildPhaseDocumentRelativePath(PipelinePhase phase)
    {
        return $"{ToStoragePhaseSegment(phase)}.json";
    }

    public static string BuildPhaseVerificationRelativePath(PipelinePhase phase)
    {
        return $"verification/{ToStoragePhaseSegment(phase)}_verification.json";
    }

    public static string BuildPhaseGateRelativePath(PipelinePhase phase)
    {
        return $"verification/{ToStoragePhaseSegment(phase)}_gate.json";
    }

    public static string BuildPhaseMetadataRelativePath(PipelinePhase phase)
    {
        return $"metadata/{ToStoragePhaseSegment(phase)}_meta.json";
    }

    public static string BuildCoverLetterHtmlRelativePath()
    {
        return "cover_letter/cover_letter.html";
    }

    public static string BuildCoverLetterCssRelativePath()
    {
        return "cover_letter/cover_letter.css";
    }

    public static string BuildCoverLetterPdfRelativePath()
    {
        return "cover_letter/cover_letter.pdf";
    }

    public static string BuildCoverLetterSummaryRelativePath()
    {
        return "cover_letter/cover_letter_render_summary.json";
    }

    public static string BuildFitAdvisoryRelativePath()
    {
        return "advisory/fit_advisory.json";
    }

    public static string BuildSafeFileName(string? fileName, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(fileName) ? fallback : fileName.Trim();
        var safe = InvalidPathCharacters.Replace(Path.GetFileName(trimmed), "-");
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    public static string BuildSafePathSegment(string? segment, string fallback)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
        {
            return fallback;
        }

        var safe = InvalidPathCharacters.Replace(segment.Trim(), "-");
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string ToStoragePhaseSegment(PipelinePhase phase)
    {
        return PipelinePhaseCatalog.ToRouteSegment(phase).Replace('-', '_');
    }

    private static string TrimSlashes(string value)
    {
        return value.Trim('/');
    }
}