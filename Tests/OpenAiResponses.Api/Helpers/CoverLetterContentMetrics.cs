using System.Text.Json;

namespace OpenAiResponses.Api.Helpers;

/// <summary>
/// Extracts the exact visible cover-letter body text that will be rendered into the HTML template.
/// </summary>
public static class CoverLetterContentMetrics
{
    public const int DefaultEstimatedCharactersPerLine = 72;

    /// <summary>
    /// Returns the paragraphs that the cover-letter template will render inside the main-content area.
    /// </summary>
    public static IReadOnlyList<CoverLetterParagraph> BuildRenderedParagraphs(JsonElement root, string? signatureName)
    {
        var paragraphs = new List<CoverLetterParagraph>();

        if (root.TryGetProperty("sections", out var sections) && sections.ValueKind == JsonValueKind.Array)
        {
            foreach (var section in sections.EnumerateArray())
            {
                var text = GetString(section, "text_da").Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var sectionKind = GetString(section, "section_kind");
                paragraphs.Add(new CoverLetterParagraph(text, string.IsNullOrWhiteSpace(sectionKind) ? null : sectionKind));

                if (string.Equals(sectionKind, "signature", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(signatureName)
                    && !text.Contains(signatureName, StringComparison.OrdinalIgnoreCase))
                {
                    paragraphs.Add(new CoverLetterParagraph(signatureName.Trim(), "signature-name"));
                }
            }
        }

        if (paragraphs.Count > 0)
        {
            return paragraphs;
        }

        var assembledApplication = GetString(root, "assembled_application_da").Trim();
        if (string.IsNullOrWhiteSpace(assembledApplication))
        {
            return [];
        }

        return assembledApplication
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => new CoverLetterParagraph(text, null))
            .ToList();
    }

    /// <summary>
    /// Counts the visible body characters that will be rendered into the template.
    /// </summary>
    public static int CountVisibleCharacters(JsonElement root, string? signatureName)
    {
        return BuildRenderedParagraphs(root, signatureName)
            .Sum(paragraph => NormalizeLineEndings(paragraph.Text).Length);
    }

    /// <summary>
    /// Computes the effective template-budget usage for the rendered content.
    /// </summary>
    public static CoverLetterBudgetMetrics CalculateBudgetMetrics(JsonElement root, string? signatureName, int estimatedCharactersPerLine)
    {
        return CalculateBudgetMetrics(BuildRenderedParagraphs(root, signatureName), estimatedCharactersPerLine);
    }

    /// <summary>
    /// Computes the effective template-budget usage for a rendered paragraph set.
    /// </summary>
    public static CoverLetterBudgetMetrics CalculateBudgetMetrics(IReadOnlyList<CoverLetterParagraph> paragraphs, int estimatedCharactersPerLine)
    {
        if (estimatedCharactersPerLine <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(estimatedCharactersPerLine), "Estimated characters per line must be greater than zero.");
        }

        var visibleCharacterCount = 0;
        var explicitLineBreakCount = 0;

        foreach (var paragraph in paragraphs)
        {
            var normalizedText = NormalizeLineEndings(paragraph.Text);
            visibleCharacterCount += normalizedText.Length;
            explicitLineBreakCount += normalizedText.Count(character => character == '\n');
        }

        var paragraphBreakCount = Math.Max(0, paragraphs.Count - 1);
        var explicitLineBreakPenalty = explicitLineBreakCount * Math.Max(0, estimatedCharactersPerLine - 1);
        var paragraphBreakPenalty = paragraphBreakCount * estimatedCharactersPerLine;

        return new CoverLetterBudgetMetrics(
            VisibleCharacterCount: visibleCharacterCount,
            ExplicitLineBreakCount: explicitLineBreakCount,
            ParagraphBreakCount: paragraphBreakCount,
            BudgetUsage: visibleCharacterCount + explicitLineBreakPenalty + paragraphBreakPenalty,
            EstimatedCharactersPerLine: estimatedCharactersPerLine);
    }

    private static string GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string NormalizeLineEndings(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
    }
}

/// <summary>
/// One paragraph emitted into the rendered cover-letter body.
/// </summary>
public readonly record struct CoverLetterParagraph(string Text, string? SectionKind);

/// <summary>
/// Layout-aware usage metrics for the rendered cover-letter body.
/// </summary>
public readonly record struct CoverLetterBudgetMetrics(
    int VisibleCharacterCount,
    int ExplicitLineBreakCount,
    int ParagraphBreakCount,
    int BudgetUsage,
    int EstimatedCharactersPerLine);