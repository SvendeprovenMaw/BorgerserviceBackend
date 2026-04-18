using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class CompanyContextService : ICompanyContextService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly OpenAIOptions _openAiOptions;

    public CompanyContextService(
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

    public async Task<StructuredJsonGenerationResult> GenerateCompanyContextAsync(CompanyContextGenerationRequest request, CancellationToken cancellationToken = default)
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
            EnableWebSearch = true,
            ForceWebSearchTool = true,
            InputTexts = BuildTextInputs(request),
            InputFiles = BuildFileInputs(request)
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    private static void ValidateRequest(CompanyContextGenerationRequest request)
    {
        var hasCompanyName = !string.IsNullOrWhiteSpace(request.CompanyName);
        var hasJobPostingText = !string.IsNullOrWhiteSpace(request.JobPostingText);
        var hasJobPostingFile = !string.IsNullOrWhiteSpace(request.JobPostingFilePath);

        if (!hasCompanyName && !hasJobPostingText && !hasJobPostingFile)
        {
            throw new ArgumentException("CompanyContext requires either a company name, job posting text, or a job posting file.", nameof(request));
        }
    }

    private List<StructuredTextInput> BuildTextInputs(CompanyContextGenerationRequest request)
    {
        var inputs = new List<StructuredTextInput>();

        if (!string.IsNullOrWhiteSpace(request.CompanyName))
        {
            inputs.Add(new StructuredTextInput
            {
                Label = "Firmanavn",
                Content = request.CompanyName.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(request.JobPostingText))
        {
            inputs.Add(new StructuredTextInput
            {
                Label = "Jobopslagstekst",
                Content = request.JobPostingText.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(request.ApplicantProfileText))
        {
            inputs.Add(new StructuredTextInput
            {
                Label = "Ansøgerens profildata",
                Content = request.ApplicantProfileText.Trim()
            });
        }

        if (!string.IsNullOrWhiteSpace(request.ApplicantAddressHint))
        {
            inputs.Add(new StructuredTextInput
            {
                Label = "Ansøgeradresse hint",
                Content = request.ApplicantAddressHint.Trim()
            });
        }

        return inputs;
    }

    private static List<StructuredFileInput> BuildFileInputs(CompanyContextGenerationRequest request)
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

        files.AddRange(request.ApplicantProfileFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new StructuredFileInput
            {
                Label = "Ansøgerprofil",
                FilePath = path
            }));

        return files;
    }

    private async Task<FlowAsset> LoadAssetAsync(CancellationToken cancellationToken)
    {
        var schemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "AI Schemas/LLM Parsing/company_context_schema.json");
        var promptPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "Prompts/company_context.prompt");
        var basePromptPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "Prompts/base.prompt");

        var schemaContent = await File.ReadAllTextAsync(schemaPath, cancellationToken);
        var basePrompt = await File.ReadAllTextAsync(basePromptPath, cancellationToken);
        var phasePrompt = await File.ReadAllTextAsync(promptPath, cancellationToken);

        using var schemaDocument = JsonDocument.Parse(schemaContent);
        var root = schemaDocument.RootElement;

        if (!root.TryGetProperty("schema", out var outputSchema))
        {
            throw new InvalidOperationException("company_context_schema.json does not contain a 'schema' property.");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : "company_context_v1";

        return new FlowAsset(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim()),
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? "company_context_v1" : schemaName,
            SchemaDescription: "Structured company context enriched with web search and applicant profile context.",
            OutputSchema: outputSchema.Clone());
    }

    private string ResolveModelId()
    {
        return _openAiOptions.ResolveModelId(_openAiOptions.Phases.CompanyContext.Model);
    }

    private sealed record FlowAsset(
        string Prompt,
        string SchemaName,
        string? SchemaDescription,
        JsonElement OutputSchema);
}