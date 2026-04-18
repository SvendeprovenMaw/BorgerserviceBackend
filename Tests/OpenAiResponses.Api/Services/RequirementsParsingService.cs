using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Runs the standalone requirements phase using the same prompt and schema assets as the sample pipeline.
/// </summary>
public sealed class RequirementsParsingService : IRequirementsParsingService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly OpenAIOptions _openAiOptions;

    public RequirementsParsingService(
        IHostEnvironment environment,
        IConfiguration configuration,
        IOpenAiResponsesService openAiResponsesService,
        IOptions<OpenAIOptions> openAiOptions)
    {
        _environment = environment;
        _configuration = configuration;
        _openAiResponsesService = openAiResponsesService;
        _openAiOptions = openAiOptions.Value;
    }

    public async Task<StructuredJsonGenerationResult> GenerateRequirementsAsync(RequirementsGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var asset = await LoadAssetAsync(cancellationToken);
        var structuredRequest = new StructuredJsonResponseRequest
        {
            Prompt = asset.Prompt,
            SchemaName = asset.SchemaName,
            SchemaDescription = asset.SchemaDescription,
            OutputSchema = asset.OutputSchema,
            Model = ResolveModelId(),
            InputTexts = BuildTextInputs(request),
            InputFiles = BuildFileInputs(request)
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    private static void ValidateRequest(RequirementsGenerationRequest request)
    {
        var hasJobPostingText = !string.IsNullOrWhiteSpace(request.JobPostingText);
        var hasJobPostingFile = !string.IsNullOrWhiteSpace(request.JobPostingFilePath);

        if (!hasJobPostingText && !hasJobPostingFile)
        {
            throw new ArgumentException("Requirements parsing requires either job posting text or a job posting file.", nameof(request));
        }
    }

    private static List<StructuredTextInput> BuildTextInputs(RequirementsGenerationRequest request)
    {
        var inputs = new List<StructuredTextInput>();

        if (!string.IsNullOrWhiteSpace(request.JobPostingText))
        {
            inputs.Add(new StructuredTextInput
            {
                Label = "Jobopslagstekst",
                Content = request.JobPostingText.Trim()
            });
        }

        return inputs;
    }

    private static List<StructuredFileInput> BuildFileInputs(RequirementsGenerationRequest request)
    {
        var files = new List<StructuredFileInput>();

        if (!string.IsNullOrWhiteSpace(request.JobPostingFilePath))
        {
            files.Add(new StructuredFileInput
            {
                Label = "Jobopslag",
                FilePath = request.JobPostingFilePath
            });
        }

        return files;
    }

    private async Task<FlowAsset> LoadAssetAsync(CancellationToken cancellationToken)
    {
        var repositoryRoot = GetRepositoryRoot();
        var schemaPath = Path.Combine(repositoryRoot, "LLM", "AI Schemas", "LLM Parsing", "requirements_schema.json");
        var promptPath = Path.Combine(repositoryRoot, "LLM", "Prompts", "requirements.prompt");
        var basePromptPath = Path.Combine(repositoryRoot, "LLM", "Prompts", "base.prompt");

        var schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        var basePrompt = await File.ReadAllTextAsync(basePromptPath, cancellationToken);
        var phasePrompt = await File.ReadAllTextAsync(promptPath, cancellationToken);

        using var schemaDocument = JsonDocument.Parse(schemaContent);
        var root = schemaDocument.RootElement;

        if (!root.TryGetProperty("schema", out var outputSchema))
        {
            throw new InvalidOperationException("requirements_schema.json does not contain a 'schema' property.");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : "job_requirements_extraction_v1";

        return new FlowAsset(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim()),
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? "job_requirements_extraction_v1" : schemaName,
            SchemaDescription: "Structured requirements extracted from a single uploaded or inline job posting.",
            OutputSchema: outputSchema.Clone());
    }

    private string ResolveModelId()
    {
        return _openAiOptions.ResolveModelId(_openAiOptions.Phases.Requirements.Model);
    }

    private string GetRepositoryRoot()
    {
        return RepositoryRootResolver.GetRepositoryRoot(_configuration, _environment);
    }

    private sealed record FlowAsset(
        string Prompt,
        string SchemaName,
        string? SchemaDescription,
        JsonElement OutputSchema);
}