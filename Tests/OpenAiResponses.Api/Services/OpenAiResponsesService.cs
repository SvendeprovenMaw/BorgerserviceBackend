using System.ClientModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

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

    public async Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var normalizedPersonFiles = NormalizeFilePaths(request.PersonFiles);
        var normalizedJobApplication = Path.GetFullPath(request.JobApplication);
        var selectedModel = string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model.Trim();
        var schemaName = SanitizeSchemaName(request.SchemaName);
        var schemaJson = request.OutputSchema.GetRawText();

        var contentParts = new List<ResponseContentPart>
        {
            ResponseContentPart.CreateInputTextPart(BuildInstructionText(request.Prompt))
        };

        foreach (var personFile in normalizedPersonFiles)
        {
            contentParts.Add(ResponseContentPart.CreateInputTextPart($"Person file: {Path.GetFileName(personFile)}"));
            contentParts.Add(await CreateFilePartAsync(personFile, cancellationToken));
        }

        contentParts.Add(ResponseContentPart.CreateInputTextPart($"Job application file: {Path.GetFileName(normalizedJobApplication)}"));
        contentParts.Add(await CreateFilePartAsync(normalizedJobApplication, cancellationToken));

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

    private static string BuildInstructionText(string prompt)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You will receive person files followed by one job application file.");
        builder.AppendLine("Use the file contents as the only source material.");
        builder.AppendLine("Return only JSON that matches the provided schema exactly.");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine(prompt.Trim());
        builder.AppendLine();
        builder.AppendLine("The following file parts are grouped by category.");
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

    private static IReadOnlyList<string> NormalizeFilePaths(IEnumerable<string> filePaths)
    {
        return filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

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

    private static void ValidateRequest(StrictJsonResponseRequest request)
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
}
