using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Generates a single-page PDF cover letter from the structured application document.
/// </summary>
public interface ICoverLetterPdfRenderer
{
    /// <summary>
    /// Produces a PDF artifact and page-limit metadata without mutating the application JSON.
    /// </summary>
    Task<CoverLetterPdfRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default);
}