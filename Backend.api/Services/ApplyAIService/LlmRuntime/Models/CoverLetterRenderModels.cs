namespace Backend.api.Services.ApplyAIService.LlmRuntime.Models;

public sealed class CoverLetterTemplateRenderResult
{
    public string HtmlDocument { get; init; } = string.Empty;

    public string StylesheetText { get; init; } = string.Empty;

    public int MainContentCharacterCount { get; init; }

    public int MainContentBudgetUsage { get; init; }

    public int MaxMainContentCharacters { get; init; }

    public int ExplicitLineBreakCount { get; init; }

    public int ParagraphBreakCount { get; init; }

    public int EstimatedCharactersPerLine { get; init; }

    public bool WithinMainContentLimit { get; init; }

    public List<string> MissingFields { get; init; } = [];

    public List<string> Warnings { get; init; } = [];
}

public sealed class CoverLetterPdfRenderResult
{
    public byte[] PdfDocument { get; init; } = [];

    public int PageCount { get; init; }

    public bool WithinSinglePageLimit { get; init; }
}