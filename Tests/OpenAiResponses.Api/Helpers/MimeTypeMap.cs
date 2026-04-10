namespace OpenAiResponses.Api.Helpers;

public static class MimeTypeMap
{
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = "text/csv",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".json"] = "application/json",
        [".md"] = "text/markdown",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".rtf"] = "application/rtf",
        [".txt"] = "text/plain",
        [".xml"] = "application/xml"
    };

    public static string GetMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return !string.IsNullOrWhiteSpace(extension) && Types.TryGetValue(extension, out var mediaType)
            ? mediaType
            : "application/octet-stream";
    }
}
