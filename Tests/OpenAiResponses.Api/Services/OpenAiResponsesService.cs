using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Converts local prompt/file inputs into Responses API requests and validates that the reply is strict JSON.
/// </summary>
public sealed class OpenAiResponsesService : IOpenAiResponsesService
{
    private readonly ResponsesClient _responsesClient;
    private readonly OpenAIOptions _options;
    private readonly ILogger<OpenAiResponsesService> _logger;

    public OpenAiResponsesService(
        ResponsesClient responsesClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAiResponsesService> logger)
    {
        _responsesClient = responsesClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Preserves the original strict-json endpoint shape by translating it into the generic request model.
    /// </summary>
    public async Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        ValidateStrictJsonRequest(request);

        var structuredRequest = new StructuredJsonResponseRequest
        {
            Prompt = request.Prompt,
            OutputSchema = request.OutputSchema,
            Model = request.Model,
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

        return await GenerateStructuredJsonAsync(structuredRequest, cancellationToken);
    }

    /// <summary>
    /// Builds the final Responses API payload from normalized text and file inputs.
    /// </summary>
    public async Task<string> GenerateStructuredJsonAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        ValidateStructuredRequest(request);

        var normalizedInputFiles = NormalizeFileInputs(request.InputFiles);
        var selectedModel = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model.Trim();
        var schemaName = SanitizeSchemaName(request.SchemaName);
        var schemaJson = request.OutputSchema.GetRawText();

        var contentParts = new List<ResponseContentPart>
        {
            ResponseContentPart.CreateInputTextPart(BuildInstructionText(request.Prompt))
        };

        // Text inputs are promoted into labeled prompt context so the model can refer to them explicitly.
        foreach (var inputText in request.InputTexts.Where(text => !string.IsNullOrWhiteSpace(text.Content)))
        {
            var label = string.IsNullOrWhiteSpace(inputText.Label) ? "Input text" : inputText.Label.Trim();
            contentParts.Add(ResponseContentPart.CreateInputTextPart($"{label}:\n{inputText.Content.Trim()}"));
        }

        // File inputs are sent twice: once as a readable label and once as the actual binary/file part.
        foreach (var inputFile in normalizedInputFiles)
        {
            contentParts.Add(ResponseContentPart.CreateInputTextPart($"{inputFile.Label}: {Path.GetFileName(inputFile.FilePath)}"));
            contentParts.Add(await CreateFilePartAsync(inputFile.FilePath, cancellationToken));
        }

        // The schema format forces the model to emit a single JSON object that matches the supplied contract.
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

        return outputJson;
    }

    /// <summary>
    /// Wraps the task prompt with fixed operating rules shared by all structured calls.
    /// </summary>
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

    /// <summary>
    /// Reads the file into a MIME-typed content part accepted by the Responses API.
    /// </summary>
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

        // Normalize to absolute paths and collapse duplicates so the same file is never uploaded twice.
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

    /// <summary>
    /// Keeps schema names compatible with the API's length and character constraints.
    /// </summary>
    private static string SanitizeSchemaName(string? schemaName)
    {
        var fallbackName = "strict_response";
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

    /// <summary>
    /// Ensures the generic request has a prompt, a schema, and at least one non-empty input.
    /// </summary>
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
