using System.ClientModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class OpenAiResponsesService : IOpenAiResponsesService
{
    private readonly ResponsesClient _responsesClient;
    private readonly OpenAIOptions _options;
    private readonly ICurrencyDisplayConversionService _currencyDisplayConversionService;
    private readonly ILogger<OpenAiResponsesService> _logger;

    public OpenAiResponsesService(
        ResponsesClient responsesClient,
        IOptions<OpenAIOptions> options,
        ICurrencyDisplayConversionService currencyDisplayConversionService,
        ILogger<OpenAiResponsesService> logger)
    {
        _responsesClient = responsesClient;
        _options = options.Value;
        _currencyDisplayConversionService = currencyDisplayConversionService;
        _logger = logger;
    }

    public async Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        var result = await GenerateStrictJsonWithMetadataAsync(request, cancellationToken);
        return result.OutputJson;
    }

    public async Task<StructuredJsonGenerationResult> GenerateStrictJsonWithMetadataAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        ValidateStrictJsonRequest(request);

        var structuredRequest = new StructuredJsonResponseRequest
        {
            Prompt = request.Prompt,
            OutputSchema = request.OutputSchema,
            Model = request.Model,
            InputCostPerMillionTokens = request.InputCostPerMillionTokens,
            CachedInputCostPerMillionTokens = request.CachedInputCostPerMillionTokens,
            OutputCostPerMillionTokens = request.OutputCostPerMillionTokens,
            SchemaName = request.SchemaName,
            SchemaDescription = request.SchemaDescription,
            InputFiles =
            [
                .. request.PersonFiles.Select(path => new StructuredFileInput
                {
                    Label = "Person file",
                    FilePath = path
                }),
                new StructuredFileInput
                {
                    Label = "Job application file",
                    FilePath = request.JobApplication
                }
            ]
        };

        return await GenerateStructuredJsonWithMetadataAsync(structuredRequest, cancellationToken);
    }

    public async Task<string> GenerateStructuredJsonAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        var result = await GenerateStructuredJsonWithMetadataAsync(request, cancellationToken);
        return result.OutputJson;
    }

    public async Task<StructuredJsonGenerationResult> GenerateStructuredJsonWithMetadataAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        ValidateStructuredRequest(request);

        var normalizedInputFiles = NormalizeFileInputs(request.InputFiles);
        var selectedModel = string.IsNullOrWhiteSpace(request.Model) ? _options.ResolveModelId() : request.Model.Trim();
        var schemaName = SanitizeSchemaName(request.SchemaName);
        var schemaJson = request.OutputSchema.GetRawText();

        var contentParts = new List<ResponseContentPart>
        {
            ResponseContentPart.CreateInputTextPart(BuildInstructionText(request.Prompt))
        };

        foreach (var inputText in request.InputTexts.Where(text => !string.IsNullOrWhiteSpace(text.Content)))
        {
            var label = string.IsNullOrWhiteSpace(inputText.Label) ? "Input text" : inputText.Label.Trim();
            contentParts.Add(ResponseContentPart.CreateInputTextPart($"{label}:\n{inputText.Content.Trim()}"));
        }

        foreach (var inputFile in normalizedInputFiles)
        {
            contentParts.Add(ResponseContentPart.CreateInputTextPart($"{inputFile.Label}: {Path.GetFileName(inputFile.FilePath)}"));
            contentParts.Add(await CreateFilePartAsync(inputFile.FilePath, cancellationToken));
        }

        var options = new CreateResponseOptions(selectedModel, [ResponseItem.CreateUserMessageItem(contentParts)])
        {
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    schemaName,
                    BinaryData.FromString(schemaJson),
                    request.SchemaDescription,
                    true)
            }
        };

        if (request.EnableWebSearch)
        {
            options.Tools.Add(ResponseTool.CreateWebSearchTool());
        }

        if (request.EnableWebSearch && request.ForceWebSearchTool)
        {
            options.ToolChoice = ResponseToolChoice.CreateWebSearchChoice();
        }

        ClientResult<ResponseResult> response = await _responsesClient.CreateResponseAsync(options, cancellationToken);
        var outputJson = response.Value.GetOutputText();

        if (string.IsNullOrWhiteSpace(outputJson))
        {
            throw new InvalidOperationException("The OpenAI response did not contain any text output.");
        }

        try
        {
            using var _ = JsonDocument.Parse(outputJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The OpenAI response was not valid JSON. Response: {Response}", outputJson);
            throw new InvalidOperationException("The OpenAI response was not valid JSON.", ex);
        }

        var actualModel = ReadStringProperty(response.Value, "Model", "ResponseModel") ?? selectedModel;
        var tokenUsage = ExtractTokenUsage(response.Value);
        var pricing = BuildPricingSnapshot(actualModel, request);
        var requestedDisplayCurrency = CurrencyCodeHelper.Normalize(_options.DisplayCurrency, pricing.Currency);
        var currencyExchange = await _currencyDisplayConversionService.GetDisplayCurrencyQuoteAsync(pricing.Currency, cancellationToken);
        var estimatedCost = BuildDisplayTokenCostSummary(tokenUsage, pricing, currencyExchange, requestedDisplayCurrency);

        if (!string.Equals(pricing.Currency, requestedDisplayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            if (currencyExchange.AppliedRate.HasValue && currencyExchange.UsingStaleRate)
            {
                _logger.LogWarning(
                    "Display-currency conversion from {SourceCurrency} to {DisplayCurrency} is using stale exchange-rate data last refreshed at {LastSuccessfulRefreshUtc}.",
                    pricing.Currency,
                    requestedDisplayCurrency,
                    currencyExchange.LastSuccessfulRefreshUtc);
            }
            else if (!currencyExchange.AppliedRate.HasValue)
            {
                _logger.LogWarning(
                    "Display-currency conversion from {SourceCurrency} to {DisplayCurrency} is unavailable. Token cost metadata will remain in {SourceCurrency}.",
                    pricing.Currency,
                    requestedDisplayCurrency,
                    pricing.Currency);
            }
        }

        return new StructuredJsonGenerationResult
        {
            OutputJson = outputJson,
            Model = actualModel,
            ResponseId = ReadStringProperty(response.Value, "Id", "ResponseId"),
            TokenUsage = tokenUsage,
            RequestedDisplayCurrency = requestedDisplayCurrency,
            DisplayCurrency = estimatedCost.Currency,
            Pricing = pricing,
            CurrencyExchange = currencyExchange,
            EstimatedCost = estimatedCost
        };
    }

    private LlmPricingSnapshot BuildPricingSnapshot(string model, StructuredJsonResponseRequest request)
    {
        var configuredModel = _options.Models.Values.FirstOrDefault(entry => string.Equals(entry.Id, model, StringComparison.OrdinalIgnoreCase));
        var normalizedInputCost = NormalizeNonNegativePrice(request.InputCostPerMillionTokens ?? configuredModel?.InputCostPerMillionTokens ?? _options.InputCostPerMillionTokens);
        var normalizedCachedInputCost = NormalizeNonNegativePrice(request.CachedInputCostPerMillionTokens ?? configuredModel?.CachedInputCostPerMillionTokens ?? _options.CachedInputCostPerMillionTokens) ?? normalizedInputCost;
        var normalizedOutputCost = NormalizeNonNegativePrice(request.OutputCostPerMillionTokens ?? configuredModel?.OutputCostPerMillionTokens ?? _options.OutputCostPerMillionTokens);

        return new LlmPricingSnapshot
        {
            Model = string.IsNullOrWhiteSpace(model) ? _options.ResolveModelId() : model.Trim(),
            Currency = CurrencyCodeHelper.Normalize(_options.PricingCurrency),
            InputCostPerMillionTokens = normalizedInputCost,
            CachedInputCostPerMillionTokens = normalizedCachedInputCost,
            OutputCostPerMillionTokens = normalizedOutputCost,
            PricingConfigured = normalizedInputCost.HasValue && normalizedOutputCost.HasValue
        };
    }

    private static decimal? NormalizeNonNegativePrice(decimal? price)
    {
        return price.HasValue && price.Value >= 0m ? price : null;
    }

    private static LlmTokenCostSummary BuildDisplayTokenCostSummary(
        LlmTokenUsage tokenUsage,
        LlmPricingSnapshot pricing,
        CurrencyExchangeRateQuote currencyExchange,
        string requestedDisplayCurrency)
    {
        var rawEstimatedCost = BuildTokenCostSummary(tokenUsage, pricing);
        if (string.Equals(rawEstimatedCost.Currency, requestedDisplayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return rawEstimatedCost with { Currency = requestedDisplayCurrency };
        }

        if (!rawEstimatedCost.PricingConfigured || !currencyExchange.AppliedRate.HasValue)
        {
            return rawEstimatedCost;
        }

        return ConvertTokenCostSummary(rawEstimatedCost, currencyExchange);
    }

    private static LlmTokenCostSummary BuildTokenCostSummary(LlmTokenUsage tokenUsage, LlmPricingSnapshot pricing)
    {
        if (!pricing.PricingConfigured || !pricing.InputCostPerMillionTokens.HasValue || !pricing.OutputCostPerMillionTokens.HasValue)
        {
            return CreateTokenCostSummary(pricing.Currency, pricingConfigured: false, inputCost: null, outputCost: null, totalCost: null);
        }

        var cachedInputTokens = Math.Max(0L, Math.Min(tokenUsage.CachedInputTokens, tokenUsage.InputTokens));
        var uncachedInputTokens = Math.Max(0L, tokenUsage.InputTokens - cachedInputTokens);
        var cachedInputPrice = pricing.CachedInputCostPerMillionTokens ?? pricing.InputCostPerMillionTokens.Value;
        var inputCost = RoundCost(
            uncachedInputTokens / 1_000_000m * pricing.InputCostPerMillionTokens.Value
            + cachedInputTokens / 1_000_000m * cachedInputPrice);
        var outputCost = RoundCost(tokenUsage.OutputTokens / 1_000_000m * pricing.OutputCostPerMillionTokens.Value);

        return CreateTokenCostSummary(
            pricing.Currency,
            pricingConfigured: true,
            inputCost,
            outputCost,
            RoundCost(inputCost + outputCost));
    }

    private static LlmTokenCostSummary ConvertTokenCostSummary(LlmTokenCostSummary summary, CurrencyExchangeRateQuote currencyExchange)
    {
        var displayCurrency = currencyExchange.TargetCurrency;
        if (string.Equals(summary.Currency, displayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return summary with { Currency = displayCurrency };
        }

        if (!summary.PricingConfigured)
        {
            return summary with { Currency = displayCurrency };
        }

        if (!currencyExchange.AppliedRate.HasValue)
        {
            return summary;
        }

        decimal? inputCost = summary.InputCost.HasValue
            ? ConvertCost(summary.InputCost.Value, currencyExchange.AppliedRate.Value)
            : null;
        decimal? outputCost = summary.OutputCost.HasValue
            ? ConvertCost(summary.OutputCost.Value, currencyExchange.AppliedRate.Value)
            : null;
        decimal? totalCost = summary.TotalCost.HasValue
            ? ConvertCost(summary.TotalCost.Value, currencyExchange.AppliedRate.Value)
            : null;

        return CreateTokenCostSummary(
            displayCurrency,
            pricingConfigured: summary.PricingConfigured,
            inputCost,
            outputCost,
            totalCost);
    }

    private static LlmTokenCostSummary CreateTokenCostSummary(
        string currency,
        bool pricingConfigured,
        decimal? inputCost,
        decimal? outputCost,
        decimal? totalCost)
    {
        decimal? roundedUpTotalCostNumeric = totalCost.HasValue
            ? RoundUpCostToTwoDecimals(totalCost.Value)
            : null;

        return new LlmTokenCostSummary
        {
            Currency = currency,
            PricingConfigured = pricingConfigured,
            InputCost = inputCost,
            OutputCost = outputCost,
            TotalCost = totalCost,
            RoundedUpTotalCost = roundedUpTotalCostNumeric.HasValue ? FormatCostToTwoDecimals(roundedUpTotalCostNumeric.Value) : null,
            RoundedUpTotalCostNumeric = roundedUpTotalCostNumeric
        };
    }

    private static decimal RoundCost(decimal value)
    {
        return decimal.Round(value, 8, MidpointRounding.AwayFromZero);
    }

    private static decimal RoundUpCostToTwoDecimals(decimal value)
    {
        var scaledValue = value * 100m;
        var roundedScaledValue = value >= 0m
            ? decimal.Ceiling(scaledValue)
            : decimal.Floor(scaledValue);

        return decimal.Round(roundedScaledValue / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatCostToTwoDecimals(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal ConvertCost(decimal amount, decimal exchangeRate)
    {
        return decimal.Round(amount * exchangeRate, 8, MidpointRounding.AwayFromZero);
    }

    private static string BuildInstructionText(string prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You will receive input text documents and/or files.");
        builder.AppendLine("Use the provided inputs as the only source material.");
        builder.AppendLine("Return only JSON that matches the provided schema exactly.");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(prompt.Trim());
        return builder.ToString();
    }

    private static async Task<ResponseContentPart> CreateFilePartAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Input file was not found: {filePath}", filePath);
        }

        var mediaType = MimeTypeMap.GetMediaType(filePath);
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var binaryData = BinaryData.FromBytes(fileBytes, mediaType);

        return ResponseContentPart.CreateInputFilePart(binaryData, mediaType, Path.GetFileName(filePath));
    }

    private static IReadOnlyList<StructuredFileInput> NormalizeFileInputs(IEnumerable<StructuredFileInput> inputFiles)
    {
        var normalizedFiles = new List<StructuredFileInput>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var inputFile in inputFiles)
        {
            if (string.IsNullOrWhiteSpace(inputFile.FilePath))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(inputFile.FilePath);
            if (!seenPaths.Add(fullPath))
            {
                continue;
            }

            normalizedFiles.Add(new StructuredFileInput
            {
                Label = string.IsNullOrWhiteSpace(inputFile.Label) ? "Input file" : inputFile.Label.Trim(),
                FilePath = fullPath
            });
        }

        return normalizedFiles;
    }

    private static LlmTokenUsage ExtractTokenUsage(object responseResult)
    {
        var usage = GetPropertyValue(responseResult, "Usage");
        var inputDetails = GetPropertyValue(usage, "InputTokenDetails", "InputDetails");
        var outputDetails = GetPropertyValue(usage, "OutputTokenDetails", "OutputDetails");

        var inputTokens = ReadLongProperty(usage, "InputTokenCount", "InputTokens", "PromptTokenCount", "PromptTokens");
        var outputTokens = ReadLongProperty(usage, "OutputTokenCount", "OutputTokens", "CompletionTokenCount", "CompletionTokens");
        var totalTokens = ReadLongProperty(usage, "TotalTokenCount", "TotalTokens", "TokenCount");
        var cachedInputTokens = ReadLongProperty(inputDetails, "CachedTokenCount", "CachedTokens", "CachedInputTokenCount");
        var reasoningTokens = ReadLongProperty(outputDetails, "ReasoningTokenCount", "ReasoningTokens");

        if (totalTokens == 0)
        {
            totalTokens = inputTokens + outputTokens;
        }

        return new LlmTokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CachedInputTokens = cachedInputTokens,
            ReasoningTokens = reasoningTokens
        };
    }

    private static object? GetPropertyValue(object? instance, params string[] propertyNames)
    {
        if (instance is null)
        {
            return null;
        }

        var type = instance.GetType();
        foreach (var propertyName in propertyNames)
        {
            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property is not null)
            {
                return property.GetValue(instance);
            }
        }

        return null;
    }

    private static long ReadLongProperty(object? instance, params string[] propertyNames)
    {
        var value = GetPropertyValue(instance, propertyNames);
        return value switch
        {
            null => 0,
            byte byteValue => byteValue,
            short shortValue => shortValue,
            int intValue => intValue,
            long longValue => longValue,
            _ when long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static string? ReadStringProperty(object? instance, params string[] propertyNames)
    {
        var value = GetPropertyValue(instance, propertyNames);
        return value is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }

    private static string SanitizeSchemaName(string? schemaName)
    {
        const string fallbackName = "strict_response";
        if (string.IsNullOrWhiteSpace(schemaName))
        {
            return fallbackName;
        }

        var sanitized = new string(schemaName
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
            .ToArray());

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return fallbackName;
        }

        return sanitized.Length <= 64 ? sanitized : sanitized[..64];
    }

    private static void ValidateStrictJsonRequest(StrictJsonResponseRequest request)
    {
        if (request.PersonFiles is null || request.PersonFiles.Count == 0)
        {
            throw new ArgumentException("At least one person file path is required.", nameof(request.PersonFiles));
        }

        if (string.IsNullOrWhiteSpace(request.JobApplication))
        {
            throw new ArgumentException("A job application file path is required.", nameof(request.JobApplication));
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("A prompt is required.", nameof(request.Prompt));
        }

        if (request.OutputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("A JSON schema is required.", nameof(request.OutputSchema));
        }

        if (request.OutputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The output schema must be a JSON object.", nameof(request.OutputSchema));
        }
    }

    private static void ValidateStructuredRequest(StructuredJsonResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new ArgumentException("A prompt is required.", nameof(request.Prompt));
        }

        if (request.OutputSchema.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ArgumentException("A JSON schema is required.", nameof(request.OutputSchema));
        }

        if (request.OutputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The output schema must be a JSON object.", nameof(request.OutputSchema));
        }

        if (request.InputTexts.All(text => string.IsNullOrWhiteSpace(text.Content))
            && request.InputFiles.All(file => string.IsNullOrWhiteSpace(file.FilePath)))
        {
            throw new ArgumentException("At least one input text or file is required.", nameof(request));
        }

        if (request.InputTexts.Any(text => string.IsNullOrWhiteSpace(text.Content)))
        {
            throw new ArgumentException("Input text content cannot be empty.", nameof(request.InputTexts));
        }

        if (request.InputFiles.Any(file => string.IsNullOrWhiteSpace(file.FilePath)))
        {
            throw new ArgumentException("Input file paths cannot be empty.", nameof(request.InputFiles));
        }
    }
}