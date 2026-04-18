using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace ApplyAI.Playwright;

public interface IJobPostingPdfRenderer
{
    Task<RenderedPdfDocument> RenderAsync(Uri url, CancellationToken cancellationToken = default, Func<string, CancellationToken, Task>? reportProgressAsync = null);
}

public sealed record RenderedPdfDocument(
    byte[] Content,
    string FileName,
    string ContentType,
    string? PageTitle);

public sealed class PlaywrightJobPostingPdfRenderer : IJobPostingPdfRenderer
{
    private static readonly Regex InvalidFileNameCharacters = new("[^A-Za-z0-9._-]", RegexOptions.Compiled);

    public async Task<RenderedPdfDocument> RenderAsync(Uri url, CancellationToken cancellationToken = default, Func<string, CancellationToken, Task>? reportProgressAsync = null)
    {
        ArgumentNullException.ThrowIfNull(url);

        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 2200 },
            Locale = "da-DK",
        });

        var page = await context.NewPageAsync();
        await ReportProgressAsync(reportProgressAsync, "Navigating URL", cancellationToken);
        var response = await page.GotoAsync(url.ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 45000,
        });

        if (response is null || !response.Ok)
        {
            throw new InvalidOperationException($"Playwright could not load the requested job-posting URL. Status: {response?.Status}.");
        }

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Screen });
        await ReportProgressAsync(reportProgressAsync, "Capturing screenshot", cancellationToken);

        var pdf = await page.PdfAsync(new PagePdfOptions
        {
            Format = "A4",
            PrintBackground = true,
            Margin = new Margin { Top = "16mm", Right = "12mm", Bottom = "16mm", Left = "12mm" },
        });

        var pageTitle = await page.TitleAsync();
        var fileName = BuildFileName(url, pageTitle);

        return new RenderedPdfDocument(pdf, fileName, "application/pdf", pageTitle);
    }

    private static string BuildFileName(Uri url, string? pageTitle)
    {
        var raw = !string.IsNullOrWhiteSpace(pageTitle)
            ? pageTitle
            : Path.GetFileName(url.AbsolutePath);

        var trimmed = string.IsNullOrWhiteSpace(raw) ? "job-posting" : raw.Trim();
        var safe = InvalidFileNameCharacters.Replace(trimmed, "-").Trim('-');
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "job-posting";
        }

        return safe.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? safe : $"{safe}.pdf";
    }

    private static Task ReportProgressAsync(
        Func<string, CancellationToken, Task>? reportProgressAsync,
        string statusMessage,
        CancellationToken cancellationToken)
    {
        return reportProgressAsync is null
            ? Task.CompletedTask
            : reportProgressAsync(statusMessage, cancellationToken);
    }
}