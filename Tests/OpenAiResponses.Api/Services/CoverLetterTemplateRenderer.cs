using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Fills the professional HTML cover-letter template from the verified application generation document.
/// </summary>
public sealed class CoverLetterTemplateRenderer : ICoverLetterTemplateRenderer
{
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly SamplePipelineOptions _samplePipelineOptions;

    public CoverLetterTemplateRenderer(IHostEnvironment environment, IConfiguration configuration, IOptions<SamplePipelineOptions> samplePipelineOptions)
    {
        _environment = environment;
        _configuration = configuration;
        _samplePipelineOptions = samplePipelineOptions.Value;
    }

    /// <summary>
    /// Produces an HTML document plus render diagnostics without modifying the application JSON.
    /// </summary>
    public async Task<CoverLetterTemplateRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var templateOptions = _samplePipelineOptions.CoverLetterTemplate;
        var templateDirectory = ResolveRepositoryPath(templateOptions.TemplatesPath);
        var htmlTemplate = await ReadRequiredTextAsync(Path.Combine(templateDirectory, templateOptions.HtmlTemplateFileName), cancellationToken);
        var stylesheetText = await ReadRequiredTextAsync(Path.Combine(templateDirectory, templateOptions.CssTemplateFileName), cancellationToken);

        using var document = JsonDocument.Parse(applicationJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("_meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Application generation document is missing the _meta object needed by the cover-letter template.");
        }

        if (!root.TryGetProperty("application_strategy", out var strategy) || strategy.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Application generation document is missing the application_strategy object needed by the cover-letter template.");
        }

        var missingFields = new List<string>();
        var warnings = new List<string>();

        var rawApplicantName = GetString(meta, "applicant_display_name");
        var applicantName = string.IsNullOrWhiteSpace(rawApplicantName) ? "Ansøger" : rawApplicantName;
        if (string.IsNullOrWhiteSpace(rawApplicantName))
        {
            missingFields.Add("_meta.applicant_display_name");
            warnings.Add("_meta.applicant_display_name var tom, så cover-letter headeren bruger fallback-navnet 'Ansøger'.");
        }

        var rawCompanyName = GetString(meta, "company_name");
        var companyName = string.IsNullOrWhiteSpace(rawCompanyName) ? "Rekrutteringsteam" : rawCompanyName;
        if (string.IsNullOrWhiteSpace(rawCompanyName))
        {
            missingFields.Add("_meta.company_name");
            warnings.Add("_meta.company_name var tom, så cover-letter headeren bruger fallbackteksten 'Rekrutteringsteam'.");
        }

        var rawPositionTitle = GetString(meta, "position_title");
        var positionTitle = string.IsNullOrWhiteSpace(rawPositionTitle) ? "Ansøgt stilling" : rawPositionTitle;
        if (string.IsNullOrWhiteSpace(rawPositionTitle))
        {
            missingFields.Add("_meta.position_title");
            warnings.Add("_meta.position_title var tom, så cover-letter headeren bruger fallbackteksten 'Ansøgt stilling'.");
        }

        var rawSubjectLine = GetString(strategy, "subject_line_da");
        var subjectLine = string.IsNullOrWhiteSpace(rawSubjectLine)
            ? $"Ansøgning til stillingen som {positionTitle}"
            : rawSubjectLine;
        if (string.IsNullOrWhiteSpace(rawSubjectLine))
        {
            missingFields.Add("application_strategy.subject_line_da");
            warnings.Add("application_strategy.subject_line_da var tom, så emnelinjen blev bygget fra stillingstitlen.");
        }

        var assembledApplication = GetString(root, "assembled_application_da").Trim();
        if (string.IsNullOrWhiteSpace(assembledApplication))
        {
            missingFields.Add("assembled_application_da");
            warnings.Add("assembled_application_da var tom, så renderer forsøgte at bygge teksten fra sections-arrayet.");
        }

        var renderedParagraphs = CoverLetterContentMetrics.BuildRenderedParagraphs(root, rawApplicantName);
        var budgetMetrics = CoverLetterContentMetrics.CalculateBudgetMetrics(
            renderedParagraphs,
            templateOptions.EstimatedCharactersPerLine);
        var mainContentCharacterCount = budgetMetrics.VisibleCharacterCount;
        var withinMainContentLimit = budgetMetrics.BudgetUsage <= templateOptions.MaxMainContentCharacters;
        if (!withinMainContentLimit)
        {
            warnings.Add(
                $"Den synlige hovedtekst er {mainContentCharacterCount} rå tegn, men {budgetMetrics.ParagraphBreakCount} paragrafskift og {budgetMetrics.ExplicitLineBreakCount} interne linjeskift løfter det effektive template-forbrug til {budgetMetrics.BudgetUsage} mod maksimum {templateOptions.MaxMainContentCharacters}. PDF-layoutet kan blive klippet i højden.");
        }

        var mainContentHtml = BuildMainContentHtml(renderedParagraphs);
        var dateText = DateTime.Today.ToString("d. MMMM yyyy", CultureInfo.GetCultureInfo("da-DK"));
        var documentTitle = subjectLine;

        var htmlDocument = htmlTemplate
            .Replace("{{document_title}}", WebUtility.HtmlEncode(documentTitle), StringComparison.Ordinal)
            .Replace("{{stylesheet_href}}", WebUtility.HtmlEncode(templateOptions.RenderedCssFileName), StringComparison.Ordinal)
            .Replace("{{applicant_name}}", WebUtility.HtmlEncode(applicantName), StringComparison.Ordinal)
            .Replace("{{company_name}}", WebUtility.HtmlEncode(companyName), StringComparison.Ordinal)
            .Replace("{{position_title}}", WebUtility.HtmlEncode(positionTitle), StringComparison.Ordinal)
            .Replace("{{subject_line_da}}", WebUtility.HtmlEncode(subjectLine), StringComparison.Ordinal)
            .Replace("{{date_da}}", WebUtility.HtmlEncode(dateText), StringComparison.Ordinal)
            .Replace("{{main_content_html}}", mainContentHtml, StringComparison.Ordinal);

        return new CoverLetterTemplateRenderResult
        {
            HtmlDocument = htmlDocument,
            StylesheetText = stylesheetText,
            MainContentCharacterCount = mainContentCharacterCount,
            MainContentBudgetUsage = budgetMetrics.BudgetUsage,
            MaxMainContentCharacters = templateOptions.MaxMainContentCharacters,
            ExplicitLineBreakCount = budgetMetrics.ExplicitLineBreakCount,
            ParagraphBreakCount = budgetMetrics.ParagraphBreakCount,
            EstimatedCharactersPerLine = budgetMetrics.EstimatedCharactersPerLine,
            WithinMainContentLimit = withinMainContentLimit,
            MissingFields = missingFields,
            Warnings = warnings
        };
    }

    private string ResolveRepositoryPath(string configuredPath)
    {
        return RepositoryRootResolver.ResolveRepositoryPath(_configuration, _environment, configuredPath);
    }

    private static async Task<string> ReadRequiredTextAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Required cover-letter template asset was not found: {filePath}", filePath);
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Cover-letter template asset was empty: {filePath}");
        }

        return content;
    }

    private static string BuildMainContentHtml(IReadOnlyList<CoverLetterParagraph> paragraphs)
    {
        if (paragraphs.Count == 0)
        {
            return "<p class=\"letter-paragraph\">Ansøgningsteksten mangler i application_generation-dokumentet.</p>";
        }

        var htmlParagraphs = paragraphs
            .Select(paragraph =>
            {
                var cssClass = string.IsNullOrWhiteSpace(paragraph.SectionKind)
                    ? "letter-paragraph"
                    : $"letter-paragraph letter-paragraph--{SanitizeCssToken(paragraph.SectionKind)}";
                return $"<p class=\"{cssClass}\">{WebUtility.HtmlEncode(paragraph.Text)}</p>";
            })
            .ToList();

        return string.Join(Environment.NewLine, htmlParagraphs);
    }

    private static string GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string SanitizeCssToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "other";
        }

        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized.Trim('-');
    }
}