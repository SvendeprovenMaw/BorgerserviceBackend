using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class CandidateEvidenceService : ICandidateEvidenceService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly OpenAIOptions _openAiOptions;

    public CandidateEvidenceService(
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

    public async Task<StructuredJsonGenerationResult> GenerateCandidateEvidenceAsync(
        CandidateEvidenceGenerationRequest request,
        CancellationToken cancellationToken = default)
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
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Krav-dokument ID",
                    Content = request.RequirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Krav-dokument JSON",
                    Content = request.RequirementsDocumentJson
                }
            ],
            InputFiles = request.CandidateFilePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new StructuredFileInput
                {
                    Label = "Kandidatfil",
                    FilePath = path
                })
                .ToList()
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    private static void ValidateRequest(CandidateEvidenceGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequirementsDocumentId))
        {
            throw new ArgumentException("Candidate evidence requires a requirements document id.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.RequirementsDocumentJson))
        {
            throw new ArgumentException("Candidate evidence requires the requirements JSON document.", nameof(request));
        }

        if (request.CandidateFilePaths.Count == 0)
        {
            throw new ArgumentException("Candidate evidence requires at least one candidate file path.", nameof(request));
        }
    }

    private async Task<FlowAsset> LoadAssetAsync(CancellationToken cancellationToken)
    {
        var schemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "AI Schemas/LLM Parsing/candidate_evidence_schema.json");
        var promptPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "Prompts/candidate_evidence.prompt");
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
            throw new InvalidOperationException("candidate_evidence_schema.json does not contain a 'schema' property.");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : "candidate_evidence_v1";

        return new FlowAsset(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim()),
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? "candidate_evidence_v1" : schemaName,
            SchemaDescription: "Requirement-focused candidate evidence extracted from candidate files.",
            OutputSchema: outputSchema.Clone());
    }

    private string ResolveModelId()
    {
        return _openAiOptions.ResolveModelId(_openAiOptions.Phases.CandidateEvidence.Model);
    }

    private sealed record FlowAsset(
        string Prompt,
        string SchemaName,
        string? SchemaDescription,
        JsonElement OutputSchema);
}