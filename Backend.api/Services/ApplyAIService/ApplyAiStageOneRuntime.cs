using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Services;

namespace Backend.api.Services.ApplyAIService;

public interface IApplyAiStageOneRuntime
{
    Task<string> GenerateRequirementsAsync(byte[] jobPostingContent, string fileName, string mediaType, CancellationToken cancellationToken = default);

    Task<string> GenerateCompanyContextAsync(
        byte[] jobPostingContent,
        string fileName,
        string mediaType,
        string? companyName,
        string? applicantProfileText,
        string? applicantAddressHint,
        CancellationToken cancellationToken = default);

    Task<ApplyAiStageVerificationResult> VerifyRequirementsAsync(
        string documentId,
        string documentJson,
        string jobPostingFileName,
        CancellationToken cancellationToken = default);
}

public sealed class ApplyAiStageOneRuntime : IApplyAiStageOneRuntime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IRequirementsParsingService _requirementsParsingService;
    private readonly ICompanyContextService _companyContextService;
    private readonly IVerificationOrchestrator _verificationOrchestrator;
    private readonly IDownstreamGateEvaluator _downstreamGateEvaluator;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public ApplyAiStageOneRuntime(
        IRequirementsParsingService requirementsParsingService,
        ICompanyContextService companyContextService,
        IVerificationOrchestrator verificationOrchestrator,
        IDownstreamGateEvaluator downstreamGateEvaluator,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        _requirementsParsingService = requirementsParsingService;
        _companyContextService = companyContextService;
        _verificationOrchestrator = verificationOrchestrator;
        _downstreamGateEvaluator = downstreamGateEvaluator;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<string> GenerateRequirementsAsync(byte[] jobPostingContent, string fileName, string mediaType, CancellationToken cancellationToken = default)
    {
        var tempDirectory = CreateTemporaryUploadDirectory();
        try
        {
            var jobPostingFilePath = await SaveUploadedFileAsync(jobPostingContent, fileName, mediaType, tempDirectory, cancellationToken);
            var result = await _requirementsParsingService.GenerateRequirementsAsync(
                new RequirementsGenerationRequest
                {
                    JobPostingFilePath = jobPostingFilePath
                },
                cancellationToken);

            return result.OutputJson;
        }
        finally
        {
            DeleteTemporaryUploadDirectory(tempDirectory);
        }
    }

    public async Task<string> GenerateCompanyContextAsync(
        byte[] jobPostingContent,
        string fileName,
        string mediaType,
        string? companyName,
        string? applicantProfileText,
        string? applicantAddressHint,
        CancellationToken cancellationToken = default)
    {
        var tempDirectory = CreateTemporaryUploadDirectory();
        try
        {
            var jobPostingFilePath = await SaveUploadedFileAsync(jobPostingContent, fileName, mediaType, tempDirectory, cancellationToken);
            var result = await _companyContextService.GenerateCompanyContextAsync(
                new CompanyContextGenerationRequest
                {
                    CompanyName = companyName,
                    JobPostingFilePath = jobPostingFilePath,
                    ApplicantProfileText = applicantProfileText,
                    ApplicantAddressHint = applicantAddressHint
                },
                cancellationToken);

            return result.OutputJson;
        }
        finally
        {
            DeleteTemporaryUploadDirectory(tempDirectory);
        }
    }

    public async Task<ApplyAiStageVerificationResult> VerifyRequirementsAsync(
        string documentId,
        string documentJson,
        string jobPostingFileName,
        CancellationToken cancellationToken = default)
    {
        var schemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "AI Schemas/LLM Parsing/requirements_schema.json");

        var verificationRequest = new StageVerificationRequest
        {
            Stage = VerificationStage.Requirements,
            DocumentId = documentId,
            DocumentJson = documentJson,
            OutputSchemaPath = schemaPath,
            ExpectedParsedFiles = string.IsNullOrWhiteSpace(jobPostingFileName) ? [] : [jobPostingFileName],
            AllowedCitationFiles = string.IsNullOrWhiteSpace(jobPostingFileName) ? [] : [jobPostingFileName],
            DisallowedCitationFiles = []
        };

        var verificationResult = await _verificationOrchestrator.VerifyStageAsync(verificationRequest, cancellationToken);
        var gateResult = _downstreamGateEvaluator.Evaluate(verificationRequest, verificationResult);

        var response = new StageVerificationResult
        {
            Stage = verificationResult.Stage,
            DocumentId = verificationResult.DocumentId,
            VerificationMode = "mechanical_with_gate",
            Status = verificationResult.Status,
            ApprovedForDownstream = verificationResult.ApprovedForDownstream && gateResult.ApprovedForDownstream,
            WarningCount = verificationResult.WarningCount,
            ErrorCount = verificationResult.ErrorCount,
            ArtifactPath = string.Empty,
            GateArtifactPath = string.Empty,
            Gate = gateResult,
            Findings = verificationResult.Findings
        };

        return new ApplyAiStageVerificationResult(
            JsonSerializer.Serialize(response, JsonOptions),
            JsonSerializer.Serialize(gateResult, JsonOptions),
            response.ApprovedForDownstream,
            response.WarningCount,
            response.ErrorCount,
            response.Status);
    }

    private static string CreateTemporaryUploadDirectory()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "applyai-stage-one-inputs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        return tempDirectory;
    }

    private static async Task<string> SaveUploadedFileAsync(byte[] content, string fileName, string mediaType, string tempDirectory, CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = $"{Guid.NewGuid():N}{ResolveExtension(mediaType)}";
        }

        var destinationPath = Path.Combine(tempDirectory, safeFileName);

        await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);
        return destinationPath;
    }

    private static string ResolveExtension(string mediaType)
    {
        return string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            ? ".pdf"
            : ".bin";
    }

    private static void DeleteTemporaryUploadDirectory(string tempDirectory)
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for transient runtime files.
        }
    }
}

public sealed record ApplyAiStageVerificationResult(
    string VerificationJson,
    string GateJson,
    bool ApprovedForDownstream,
    int WarningCount,
    int ErrorCount,
    string Status);