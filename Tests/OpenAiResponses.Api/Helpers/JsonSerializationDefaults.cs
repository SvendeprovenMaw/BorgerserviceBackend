using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace OpenAiResponses.Api.Helpers;

/// <summary>
/// Central JSON defaults for human-readable UTF-8 output in files and API responses.
/// </summary>
public static class JsonSerializationDefaults
{
    /// <summary>
    /// JSON content type with explicit UTF-8 charset.
    /// </summary>
    public const string JsonUtf8ContentType = "application/json; charset=utf-8";

    /// <summary>
    /// Shared serializer options that keep Unicode characters readable instead of escaping them unnecessarily.
    /// </summary>
    public static readonly JsonSerializerOptions IndentedUtf8 = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>
    /// Shared UTF-8 encoding for persisted artifacts without a BOM prefix.
    /// </summary>
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Rewrites a JSON string using the shared UTF-8-friendly serializer settings.
    /// </summary>
    public static string FormatJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, IndentedUtf8);
    }
}