using System.Globalization;
using System.Text.Json;
using Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public sealed class CoverLetterPdfRenderer : ICoverLetterPdfRenderer
{
    private const string Ink = "#171717";
    private const string Muted = "#63635e";
    private const string Line = "#d8d4ca";
    private const string Accent = "#1f4b63";
    private const string AccentSoft = "#eef2f3";
    private const string SubjectCard = "#f2efe7";
    private const string FontFamily = "DejaVu Sans Mono";

    public Task<CoverLetterPdfRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = JsonDocument.Parse(applicationJson);
        var root = document.RootElement;

        if (!root.TryGetProperty("_meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Application generation document is missing the _meta object needed by the cover-letter PDF renderer.");
        }

        if (!root.TryGetProperty("application_strategy", out var strategy) || strategy.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Application generation document is missing the application_strategy object needed by the cover-letter PDF renderer.");
        }

        var rawApplicantName = GetString(meta, "applicant_display_name");
        var applicantName = string.IsNullOrWhiteSpace(rawApplicantName) ? "Ansoger" : rawApplicantName.Trim();

        var rawCompanyName = GetString(meta, "company_name");
        var companyName = string.IsNullOrWhiteSpace(rawCompanyName) ? "Rekrutteringsteam" : rawCompanyName.Trim();

        var rawPositionTitle = GetString(meta, "position_title");
        var positionTitle = string.IsNullOrWhiteSpace(rawPositionTitle) ? "Ansogt stilling" : rawPositionTitle.Trim();

        var rawSubjectLine = GetString(strategy, "subject_line_da");
        var subjectLine = string.IsNullOrWhiteSpace(rawSubjectLine)
            ? $"Ansogning til stillingen som {positionTitle}"
            : rawSubjectLine.Trim();

        var renderedParagraphs = CoverLetterContentMetrics.BuildRenderedParagraphs(root, rawApplicantName);
        var dateText = DateTime.Today.ToString("d. MMMM yyyy", CultureInfo.GetCultureInfo("da-DK"));

        var pdfDocument = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(16, Unit.Millimetre);
                page.MarginVertical(14, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(TextStyle.Default.FontFamily(FontFamily).FontSize(10.5f).FontColor(Ink));

                page.Content()
                    .ShowEntire()
                    .ScaleToFit()
                    .Element(content => ComposeDocument(
                        content,
                        applicantName,
                        companyName,
                        positionTitle,
                        subjectLine,
                        dateText,
                        renderedParagraphs));
            });
        }).GeneratePdf();

        return Task.FromResult(new CoverLetterPdfRenderResult
        {
            PdfDocument = pdfDocument,
            PageCount = 1,
            WithinSinglePageLimit = true
        });
    }

    private static void ComposeDocument(
        IContainer container,
        string applicantName,
        string companyName,
        string positionTitle,
        string subjectLine,
        string dateText,
        IReadOnlyList<CoverLetterParagraph> paragraphs)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Element(item => ComposeLetterhead(item, applicantName, companyName, positionTitle, dateText));
            column.Item().Element(item => ComposeSubjectCard(item, subjectLine));
            column.Item().Element(item => ComposeBody(item, paragraphs));
            column.Item().Element(item => ComposeFooter(item, applicantName, companyName));
        });
    }

    private static void ComposeLetterhead(
        IContainer container,
        string applicantName,
        string companyName,
        string positionTitle,
        string dateText)
    {
        container
            .BorderBottom(1)
            .BorderColor(Line)
            .PaddingBottom(12)
            .Row(row =>
            {
                row.Spacing(12);

                row.RelativeItem()
                    .BorderLeft(4)
                    .BorderColor(Accent)
                    .PaddingLeft(10)
                    .Column(column =>
                    {
                        column.Spacing(4);
                        column.Item().Text("Job Application").FontSize(8).FontColor(Muted).SemiBold();
                        column.Item().Text(applicantName).FontSize(18).Bold();
                        column.Item().Text("Professionel ansogning klargjort til A4 / PDF").FontSize(9.2f).FontColor(Muted);
                    });

                row.ConstantItem(180)
                    .Column(column =>
                    {
                        column.Spacing(6);
                        column.Item().Element(item => ComposeMetaCard(item, "Dato", dateText));
                        column.Item().Element(item => ComposeMetaCard(item, "Stilling", positionTitle));
                        column.Item().Element(item => ComposeMetaCard(item, "Virksomhed", companyName));
                    });
            });
    }

    private static void ComposeMetaCard(IContainer container, string label, string value)
    {
        container
            .Border(1)
            .BorderColor(Line)
            .Background(AccentSoft)
            .PaddingVertical(8)
            .PaddingHorizontal(10)
            .Column(column =>
            {
                column.Spacing(2);
                column.Item().Text(label).FontSize(7.8f).FontColor(Muted).SemiBold();
                column.Item().Text(value).FontSize(10).SemiBold();
            });
    }

    private static void ComposeSubjectCard(IContainer container, string subjectLine)
    {
        container
            .Border(1)
            .BorderColor(Line)
            .Background(SubjectCard)
            .PaddingVertical(10)
            .PaddingHorizontal(12)
            .Column(column =>
            {
                column.Spacing(3);
                column.Item().Text("Subject").FontSize(8).FontColor(Muted).SemiBold();
                column.Item().Text(subjectLine).FontSize(11.2f).Bold().LineHeight(1.25f);
            });
    }

    private static void ComposeBody(IContainer container, IReadOnlyList<CoverLetterParagraph> paragraphs)
    {
        container
            .Border(1)
            .BorderColor(Line)
            .PaddingVertical(14)
            .PaddingHorizontal(15)
            .Column(column =>
            {
                column.Spacing(0);

                if (paragraphs.Count == 0)
                {
                    column.Item().Text("Ansogningsteksten mangler i application_generation-dokumentet.").LineHeight(1.38f);
                    return;
                }

                foreach (var paragraph in paragraphs)
                {
                    var paragraphContainer = column.Item()
                        .PaddingBottom(GetParagraphBottomPadding(paragraph.SectionKind));

                    var paragraphText = paragraphContainer
                        .Text(paragraph.Text)
                        .LineHeight(1.38f)
                        .FontSize(GetParagraphFontSize(paragraph.SectionKind));

                    if (IsBoldParagraph(paragraph.SectionKind))
                    {
                        paragraphText.Bold();
                    }
                }
            });
    }

    private static void ComposeFooter(IContainer container, string applicantName, string companyName)
    {
        container
            .BorderTop(1)
            .BorderColor(Line)
            .PaddingTop(8)
            .Row(row =>
            {
                row.RelativeItem().Text(applicantName).FontSize(8).FontColor(Muted).SemiBold();
                row.RelativeItem().AlignRight().Text(companyName).FontSize(8).FontColor(Muted).SemiBold();
            });
    }

    private static float GetParagraphBottomPadding(string? sectionKind)
    {
        return sectionKind switch
        {
            "salutation" => 14,
            "closing" => 7,
            "signature" => 4,
            "signature_name" => 0,
            _ => 11
        };
    }

    private static float GetParagraphFontSize(string? sectionKind)
    {
        return sectionKind switch
        {
            "signature_name" => 10.5f,
            _ => 10.5f
        };
    }

    private static bool IsBoldParagraph(string? sectionKind)
    {
        return string.Equals(sectionKind, "signature_name", StringComparison.Ordinal);
    }

    private static string GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}