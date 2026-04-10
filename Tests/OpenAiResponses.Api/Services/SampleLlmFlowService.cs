using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

public sealed class SampleLlmFlowService : ISampleLlmFlowService
{
    private const string SampleCandidateDirectory = "Borger1";
    private const string PreferencesFileName = "Preferences.json";
    private static readonly JsonSerializerOptions SavedJsonOptions = new() { WriteIndented = true };
    private static readonly Lock ResultsDirectoryLock = new();

    private readonly IHostEnvironment _environment;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly ILogger<SampleLlmFlowService> _logger;
    private readonly OpenAIOptions _openAiOptions;

    public SampleLlmFlowService(
        IHostEnvironment environment,
        IOpenAiResponsesService openAiResponsesService,
        IOptions<OpenAIOptions> openAiOptions,
        ILogger<SampleLlmFlowService> logger)
    {
        _environment = environment;
        _openAiResponsesService = openAiResponsesService;
        _openAiOptions = openAiOptions.Value;
        _logger = logger;
    }

    public Task<string> RunRequirementsParsingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        return RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
    }

    public async Task<string> RunCandidateEvidenceAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));

        return await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);
    }

    public async Task<string> RunMatchingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceJson = await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);

        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        return await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            cancellationToken);
    }

    public async Task<string> RunPipelineAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        var matchingDocumentId = BuildDocumentId(
            "matching",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var applicationDocumentId = BuildDocumentId(
            "application",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var runDirectory = CreateNextRunDirectory();
        var runDirectoryRelativePath = Path.GetRelativePath(GetRepositoryRoot(), runDirectory);

        _logger.LogInformation("Pipeline run directory created at {RunDirectory}.", runDirectoryRelativePath);

        _logger.LogInformation(
            "Flow 1/4 requirements parsing: sending job application {JobApplicationFile} to OpenAI model {Model}.",
            Path.GetFileName(sampleData.JobApplication),
            _openAiOptions.Model);

        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        await SaveJsonResultAsync(runDirectory, "requirements.json", requirementsJson, cancellationToken);

        _logger.LogInformation("Flow 1/4 requirements parsing: OpenAI replied.");
        _logger.LogInformation("Flow 2/4 candidate evidence: started.");

        var candidateEvidenceJson = await RunCandidateEvidenceCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "candidate_evidence.json", candidateEvidenceJson, cancellationToken);

        _logger.LogInformation("Flow 2/4 candidate evidence: completed.");
        _logger.LogInformation("Flow 3/4 matching: started.");

        var matchJson = await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "matching.json", matchJson, cancellationToken);

        _logger.LogInformation("Flow 3/4 matching: completed.");
        _logger.LogInformation("Flow 4/4 application generation: started.");

        var applicationJson = await RunApplicationGenerationCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            matchJson,
            matchingDocumentId,
            applicationDocumentId,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "application_generation.json", applicationJson, cancellationToken);

        _logger.LogInformation("Flow 4/4 application generation: completed.");
        _logger.LogInformation("Pipeline results saved to {RunDirectory}.", runDirectoryRelativePath);

        return applicationJson;
    }

    private async Task<string> RunMatchingCoreAsync(
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        CancellationToken cancellationToken)
    {
        var matchingAsset = await LoadPhaseAssetAsync(
            promptFileName: "matching.prompt",
            schemaFileName: "matching_schema.json",
            cancellationToken);

        var request = new StructuredJsonResponseRequest
        {
            Prompt = matchingAsset.Prompt,
            SchemaName = matchingAsset.SchemaName,
            SchemaDescription = matchingAsset.SchemaDescription,
            OutputSchema = matchingAsset.OutputSchema,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Krav-dokument ID",
                    Content = requirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Krav-dokument JSON",
                    Content = requirementsJson
                },
                new StructuredTextInput
                {
                    Label = "Kandidat-evidens dokument ID",
                    Content = candidateEvidenceDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Kandidat-evidens dokument JSON",
                    Content = candidateEvidenceJson
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunApplicationGenerationCoreAsync(
        SampleDataContext sampleData,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string matchingJson,
        string matchingDocumentId,
        string applicationDocumentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sampleData.PreferencesFilePath))
        {
            throw new FileNotFoundException(
                $"Application generation preferences file was not found in sample candidate directory: {sampleData.CandidateDirectoryPath}",
                sampleData.CandidateDirectoryPath);
        }

        var applicationAsset = await LoadPhaseAssetAsync(
            promptFileName: "application_generation.prompt",
            schemaFileName: "application_generation_schema.json",
            cancellationToken);

        var preferencesJson = await ReadRequiredTextAsync(sampleData.PreferencesFilePath, cancellationToken);
        var request = new StructuredJsonResponseRequest
        {
            Prompt = applicationAsset.Prompt,
            SchemaName = applicationAsset.SchemaName,
            SchemaDescription = applicationAsset.SchemaDescription,
            OutputSchema = applicationAsset.OutputSchema,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Application document ID",
                    Content = applicationDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document ID",
                    Content = requirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document JSON",
                    Content = requirementsJson
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document ID",
                    Content = candidateEvidenceDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document JSON",
                    Content = candidateEvidenceJson
                },
                new StructuredTextInput
                {
                    Label = "Matching document ID",
                    Content = matchingDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Matching document JSON",
                    Content = matchingJson
                },
                new StructuredTextInput
                {
                    Label = "Application generation preferences JSON",
                    Content = preferencesJson
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunRequirementsParsingCoreAsync(SampleDataContext sampleData, CancellationToken cancellationToken)
    {
        var requirementsAsset = await LoadPhaseAssetAsync(
            promptFileName: "requirements.prompt",
            schemaFileName: "requirements_schema.json",
            cancellationToken);

        var request = new StructuredJsonResponseRequest
        {
            Prompt = requirementsAsset.Prompt,
            SchemaName = requirementsAsset.SchemaName,
            SchemaDescription = requirementsAsset.SchemaDescription,
            OutputSchema = requirementsAsset.OutputSchema,
            InputFiles =
            [
                new StructuredFileInput
                {
                    Label = "Job application file",
                    FilePath = sampleData.JobApplication
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunCandidateEvidenceCoreAsync(
        SampleDataContext sampleData,
        string requirementsJson,
        string requirementsDocumentId,
        CancellationToken cancellationToken)
    {
        var candidateEvidenceAsset = await LoadPhaseAssetAsync(
            promptFileName: "candidate_evidence.prompt",
            schemaFileName: "candidate_evidence_schema.json",
            cancellationToken);

        var request = new StructuredJsonResponseRequest
        {
            Prompt = candidateEvidenceAsset.Prompt,
            SchemaName = candidateEvidenceAsset.SchemaName,
            SchemaDescription = candidateEvidenceAsset.SchemaDescription,
            OutputSchema = candidateEvidenceAsset.OutputSchema,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Krav-dokument ID",
                    Content = requirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Krav-dokument JSON",
                    Content = requirementsJson
                }
            ],
            InputFiles = sampleData.PersonFiles
                .Select(path => new StructuredFileInput
                {
                    Label = "Kandidatfil",
                    FilePath = path
                })
                .ToList()
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<FlowAsset> LoadPhaseAssetAsync(string promptFileName, string schemaFileName, CancellationToken cancellationToken)
    {
        var repositoryRoot = GetRepositoryRoot();
        var schemaContent = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "AI Schemas", "LLM Parsing", schemaFileName), cancellationToken);
        var combinedPrompt = await LoadCombinedPromptAsync(promptFileName, cancellationToken);

        using var schemaDocument = JsonDocument.Parse(schemaContent);
        var root = schemaDocument.RootElement;

        if (!root.TryGetProperty("schema", out var outputSchema))
        {
            throw new InvalidOperationException($"Schema file does not contain a 'schema' property: {schemaFileName}");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : null;

        return new FlowAsset(
            Prompt: combinedPrompt,
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? Path.GetFileNameWithoutExtension(schemaFileName) : schemaName,
            SchemaDescription: null,
            OutputSchema: outputSchema.Clone());
    }

    private async Task<string> LoadCombinedPromptAsync(string promptFileName, CancellationToken cancellationToken)
    {
        var repositoryRoot = GetRepositoryRoot();
        var basePrompt = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "Prompts", "base.prompt"), cancellationToken);
        var phasePrompt = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "Prompts", promptFileName), cancellationToken);

        return string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim());
    }

    private SampleDataContext GetSampleData()
    {
        var repositoryRoot = GetRepositoryRoot();
        var personDirectory = Path.Combine(repositoryRoot, "TestData", "Borgere", SampleCandidateDirectory);
        var jobDirectory = Path.Combine(repositoryRoot, "TestData", "Opslag");

        if (!Directory.Exists(personDirectory))
        {
            throw new FileNotFoundException($"Sample candidate directory was not found: {personDirectory}", personDirectory);
        }

        if (!Directory.Exists(jobDirectory))
        {
            throw new FileNotFoundException($"Sample job directory was not found: {jobDirectory}", jobDirectory);
        }

        var personFiles = Directory.GetFiles(personDirectory)
            .Where(path => !string.Equals(Path.GetFileName(path), PreferencesFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var preferencesFilePath = Directory.GetFiles(personDirectory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), PreferencesFileName, StringComparison.OrdinalIgnoreCase));

        if (personFiles.Length == 0)
        {
            throw new FileNotFoundException($"No candidate files were found in: {personDirectory}", personDirectory);
        }

        var jobApplication = Directory.GetFiles(jobDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(jobApplication))
        {
            throw new FileNotFoundException($"No job application files were found in: {jobDirectory}", jobDirectory);
        }

        return new SampleDataContext(personDirectory, personFiles, jobApplication, preferencesFilePath);
    }

    private string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", ".."));
    }

    private string CreateNextRunDirectory()
    {
        var resultsRoot = Path.Combine(GetRepositoryRoot(), "LLM", "Results");
        Directory.CreateDirectory(resultsRoot);

        lock (ResultsDirectoryLock)
        {
            var nextRunNumber = Directory.GetDirectories(resultsRoot, "Run *", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Select(TryParseRunNumber)
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .DefaultIfEmpty(0)
                .Max() + 1;

            while (true)
            {
                var runDirectory = Path.Combine(resultsRoot, $"Run {nextRunNumber}");
                if (!Directory.Exists(runDirectory))
                {
                    Directory.CreateDirectory(runDirectory);
                    return runDirectory;
                }

                nextRunNumber++;
            }
        }
    }

    private static string BuildDocumentId(string prefix, string source)
    {
        var normalized = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');

        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? prefix : $"{prefix}_{normalized}";
    }

    private static async Task<string> ReadRequiredTextAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Required asset file was not found: {filePath}", filePath);
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Required asset file was empty: {filePath}");
        }

        return content;
    }

    private async Task SaveJsonResultAsync(string runDirectory, string fileName, string json, CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(runDirectory, fileName);
        var destinationRelativePath = Path.GetRelativePath(GetRepositoryRoot(), destinationPath);
        var formattedJson = FormatJson(json);

        await File.WriteAllTextAsync(destinationPath, formattedJson + Environment.NewLine, cancellationToken);

        _logger.LogInformation("Saved pipeline result to {ResultFile}.", destinationRelativePath);
    }

    private static string FormatJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, SavedJsonOptions);
    }

    private static int? TryParseRunNumber(string? directoryName)
    {
        const string prefix = "Run ";

        if (string.IsNullOrWhiteSpace(directoryName) || !directoryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(directoryName[prefix.Length..], out var number) && number > 0
            ? number
            : null;
    }

    private sealed record FlowAsset(string Prompt, string SchemaName, string? SchemaDescription, JsonElement OutputSchema);

    private sealed record SampleDataContext(
        string CandidateDirectoryPath,
        IReadOnlyList<string> PersonFiles,
        string JobApplication,
        string? PreferencesFilePath)
    {
        public string CandidateDirectoryName => Path.GetFileName(CandidateDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}