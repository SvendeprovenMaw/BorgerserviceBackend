using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class MatchingService : IMatchingService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly OpenAIOptions _openAiOptions;

    public MatchingService(
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

    public async Task<StructuredJsonGenerationResult> GenerateMatchingAsync(
        MatchingGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var asset = await LoadAssetAsync(cancellationToken);
        var inputTexts = new List<StructuredTextInput>
        {
            new()
            {
                Label = "Krav-dokument ID",
                Content = request.RequirementsDocumentId
            },
            new()
            {
                Label = "Krav-dokument JSON",
                Content = request.RequirementsDocumentJson
            },
            new()
            {
                Label = "Kandidat-evidens dokument ID",
                Content = request.CandidateEvidenceDocumentId
            },
            new()
            {
                Label = "Kandidat-evidens dokument JSON",
                Content = request.CandidateEvidenceDocumentJson
            }
        };

        if (!string.IsNullOrWhiteSpace(request.RegenerationFeedbackJson))
        {
            inputTexts.Add(new StructuredTextInput
            {
                Label = "Matching regeneration feedback JSON",
                Content = request.RegenerationFeedbackJson
            });
        }

        var structuredRequest = new StructuredJsonResponseRequest
        {
            Prompt = asset.Prompt,
            SchemaName = asset.SchemaName,
            SchemaDescription = asset.SchemaDescription,
            OutputSchema = asset.OutputSchema,
            Model = ResolveModelId(),
            InputTexts = inputTexts
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    private static void ValidateRequest(MatchingGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequirementsDocumentId)
            || string.IsNullOrWhiteSpace(request.RequirementsDocumentJson)
            || string.IsNullOrWhiteSpace(request.CandidateEvidenceDocumentId)
            || string.IsNullOrWhiteSpace(request.CandidateEvidenceDocumentJson))
        {
            throw new ArgumentException("Matching requires requirements and candidate evidence document ids plus JSON payloads.", nameof(request));
        }
    }

    private async Task<FlowAsset> LoadAssetAsync(CancellationToken cancellationToken)
    {
        var schemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "AI Schemas/LLM Parsing/matching_schema.json");
        var promptPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "Prompts/matching.prompt");
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
            throw new InvalidOperationException("matching_schema.json does not contain a 'schema' property.");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : "matching_v1";

        return new FlowAsset(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim()),
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? "matching_v1" : schemaName,
            SchemaDescription: "Requirement-level matching between extracted requirements and approved candidate evidence.",
            OutputSchema: outputSchema.Clone());
    }

    private string ResolveModelId()
    {
        return _openAiOptions.ResolveModelId(_openAiOptions.Phases.Matching.Model);
    }

    private sealed record FlowAsset(
        string Prompt,
        string SchemaName,
        string? SchemaDescription,
        JsonElement OutputSchema);
}