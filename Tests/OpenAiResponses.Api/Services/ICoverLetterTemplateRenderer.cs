using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Renders the generated application JSON into a print-friendly HTML cover letter.
/// </summary>
public interface ICoverLetterTemplateRenderer
{
    /// <summary>
    /// Applies the configured HTML and CSS template to the generated application document.
    /// </summary>
    Task<CoverLetterTemplateRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default);
}