using System.Text.Json;
using System.Text.Json.Nodes;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class ApplicationGenerationService : IApplicationGenerationService
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly OpenAIOptions _openAiOptions;

    public ApplicationGenerationService(
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

    public async Task<StructuredJsonGenerationResult> GenerateApplicationGenerationAsync(
        ApplicationGenerationRequest request,
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
            InputCostPerMillionTokens = _openAiOptions.Phases.ApplicationGeneration.InputCostPerMillionTokens,
            CachedInputCostPerMillionTokens = _openAiOptions.Phases.ApplicationGeneration.CachedInputCostPerMillionTokens,
            OutputCostPerMillionTokens = _openAiOptions.Phases.ApplicationGeneration.OutputCostPerMillionTokens,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Application document ID",
                    Content = request.ApplicationDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document ID",
                    Content = request.RequirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document JSON",
                    Content = request.RequirementsDocumentJson
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document ID",
                    Content = request.CandidateEvidenceDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document JSON",
                    Content = request.CandidateEvidenceDocumentJson
                },
                new StructuredTextInput
                {
                    Label = "Company context document ID",
                    Content = request.CompanyContextDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Company context document JSON",
                    Content = request.CompanyContextDocumentJson
                },
                new StructuredTextInput
                {
                    Label = "Matching document ID",
                    Content = request.MatchingDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Matching document JSON",
                    Content = request.MatchingDocumentJson
                },
                new StructuredTextInput
                {
                    Label = "Application generation preferences JSON",
                    Content = NormalizePreferencesJson(request.PreferencesJson)
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    private static void ValidateRequest(ApplicationGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationDocumentId)
            || string.IsNullOrWhiteSpace(request.RequirementsDocumentId)
            || string.IsNullOrWhiteSpace(request.RequirementsDocumentJson)
            || string.IsNullOrWhiteSpace(request.CandidateEvidenceDocumentId)
            || string.IsNullOrWhiteSpace(request.CandidateEvidenceDocumentJson)
            || string.IsNullOrWhiteSpace(request.CompanyContextDocumentId)
            || string.IsNullOrWhiteSpace(request.CompanyContextDocumentJson)
            || string.IsNullOrWhiteSpace(request.MatchingDocumentId)
            || string.IsNullOrWhiteSpace(request.MatchingDocumentJson)
            || string.IsNullOrWhiteSpace(request.PreferencesJson))
        {
            throw new ArgumentException("Application generation requires upstream document ids, upstream JSON payloads, and preferences JSON.", nameof(request));
        }
    }

    private async Task<FlowAsset> LoadAssetAsync(CancellationToken cancellationToken)
    {
        var schemaPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "AI Schemas/LLM Parsing/application_generation_schema.json");
        var promptPath = ApplyAiAssetPathResolver.ResolveCatalogPath(
            _configuration,
            _environment,
            "Prompts/application_generation.prompt");
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
            throw new InvalidOperationException("application_generation_schema.json does not contain a 'schema' property.");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : "application_generation_v1";

        return new FlowAsset(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim()),
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? "application_generation_v1" : schemaName,
            SchemaDescription: "Structured application-generation output for the final cover-letter phase.",
            OutputSchema: outputSchema.Clone());
    }

    private static string NormalizePreferencesJson(string preferencesJson)
    {
        JsonNode? parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(preferencesJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Application generation preferences did not contain valid JSON.", exception);
        }

        if (parsedNode is not JsonObject root)
        {
            throw new InvalidOperationException("Application generation preferences must contain a JSON object at the root.");
        }

        var contentConstraints = root["content_constraints"] as JsonObject;
        if (contentConstraints is null)
        {
            contentConstraints = new JsonObject();
            root["content_constraints"] = contentConstraints;
        }

        int? configuredMax = null;
        if (contentConstraints["max_main_content_characters"] is JsonNode existingMaxNode)
        {
            configuredMax = existingMaxNode.GetValue<int>();
        }

        if (!configuredMax.HasValue || configuredMax.Value > CoverLetterContentMetrics.DefaultMaxMainContentCharacters)
        {
            contentConstraints["max_main_content_characters"] = CoverLetterContentMetrics.DefaultMaxMainContentCharacters;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private string ResolveModelId()
    {
        return _openAiOptions.ResolveModelId(_openAiOptions.Phases.ApplicationGeneration.Model);
    }

    private sealed record FlowAsset(
        string Prompt,
        string SchemaName,
        string? SchemaDescription,
        JsonElement OutputSchema);
}