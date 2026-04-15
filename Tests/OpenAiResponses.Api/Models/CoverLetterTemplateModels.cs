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
    /// Number of raw characters in the visible main content payload.
    /// </summary>
    public int MainContentCharacterCount { get; init; }

    /// <summary>
    /// Effective layout-aware budget usage for the visible main content.
    /// </summary>
    public int MainContentBudgetUsage { get; init; }

    /// <summary>
    /// Configured maximum number of monospace character-units allowed in the main content field.
    /// </summary>
    public int MaxMainContentCharacters { get; init; }

    /// <summary>
    /// Number of explicit line breaks inside paragraph text.
    /// </summary>
    public int ExplicitLineBreakCount { get; init; }

    /// <summary>
    /// Number of paragraph breaks between rendered paragraphs.
    /// </summary>
    public int ParagraphBreakCount { get; init; }

    /// <summary>
    /// Estimated monospace characters that fit on one rendered line.
    /// </summary>
    public int EstimatedCharactersPerLine { get; init; }

    /// <summary>
    /// Whether the rendered content stays within the configured layout budget.
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

/// <summary>
/// Persisted summary of the generated cover-letter artifacts for a pipeline run.
/// </summary>
public sealed class CoverLetterRenderArtifact
{
    /// <summary>
    /// Overall artifact status.
    /// </summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the rendered HTML artifact.
    /// </summary>
    public string? HtmlArtifactPath { get; init; }

    /// <summary>
    /// Relative path to the rendered CSS artifact.
    /// </summary>
    public string? CssArtifactPath { get; init; }

    /// <summary>
    /// Relative path to the rendered PDF artifact.
    /// </summary>
    public string? PdfArtifactPath { get; init; }

    /// <summary>
    /// Number of pages in the rendered PDF when generation succeeded.
    /// </summary>
    public int? PdfPageCount { get; init; }

    /// <summary>
    /// Whether the PDF stayed within the one-page limit.
    /// </summary>
    public bool? WithinSinglePageLimit { get; init; }

    /// <summary>
    /// Number of raw characters in the visible main content payload.
    /// </summary>
    public int? MainContentCharacterCount { get; init; }

    /// <summary>
    /// Effective layout-aware budget usage for the visible main content.
    /// </summary>
    public int? MainContentBudgetUsage { get; init; }

    /// <summary>
    /// Configured maximum number of monospace character-units allowed in the main content field.
    /// </summary>
    public int? MaxMainContentCharacters { get; init; }

    /// <summary>
    /// Number of explicit line breaks inside paragraph text.
    /// </summary>
    public int? ExplicitLineBreakCount { get; init; }

    /// <summary>
    /// Number of paragraph breaks between rendered paragraphs.
    /// </summary>
    public int? ParagraphBreakCount { get; init; }

    /// <summary>
    /// Estimated monospace characters that fit on one rendered line.
    /// </summary>
    public int? EstimatedCharactersPerLine { get; init; }

    /// <summary>
    /// Whether the rendered content stays within the configured layout budget.
    /// </summary>
    public bool? WithinMainContentLimit { get; init; }

    /// <summary>
    /// Fields that were absent in the application document and had to use a fallback.
    /// </summary>
    public List<string> MissingFields { get; init; } = [];

    /// <summary>
    /// Non-fatal observations from HTML or PDF rendering.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Non-fatal PDF generation error when HTML/CSS rendering still succeeded.
    /// </summary>
    public string? PdfErrorMessage { get; init; }

    /// <summary>
    /// Fatal render error when the overall artifact step failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Binary PDF output plus page-limit metadata for the cover-letter document.
/// </summary>
public sealed class CoverLetterPdfRenderResult
{
    /// <summary>
    /// Raw PDF bytes.
    /// </summary>
    public byte[] PdfDocument { get; init; } = [];

    /// <summary>
    /// Number of pages in the generated PDF.
    /// </summary>
    public int PageCount { get; init; }

    /// <summary>
    /// Whether the generated PDF satisfies the configured one-page constraint.
    /// </summary>
    public bool WithinSinglePageLimit { get; init; }
}