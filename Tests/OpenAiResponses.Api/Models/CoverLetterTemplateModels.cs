namespace OpenAiResponses.Api.Models;

/// <summary>
/// Result of rendering a professional cover-letter HTML document from the application generation JSON.
/// </summary>
public sealed class CoverLetterTemplateRenderResult
{
    /// <summary>
    /// Final rendered HTML document.
    /// </summary>
    public string HtmlDocument { get; init; } = string.Empty;

    /// <summary>
    /// Stylesheet used by the rendered document.
    /// </summary>
    public string StylesheetText { get; init; } = string.Empty;

    /// <summary>
    /// Number of characters in the visible main content payload.
    /// </summary>
    public int MainContentCharacterCount { get; init; }

    /// <summary>
    /// Configured maximum number of characters allowed in the main content field.
    /// </summary>
    public int MaxMainContentCharacters { get; init; }

    /// <summary>
    /// Whether the rendered content stays within the configured character cap.
    /// </summary>
    public bool WithinMainContentLimit { get; init; }

    /// <summary>
    /// Fields that were absent in the application document and had to use a fallback.
    /// </summary>
    public List<string> MissingFields { get; init; } = [];

    /// <summary>
    /// Non-fatal observations from the rendering step.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}