using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface ICoverLetterPdfRenderer
{
    Task<CoverLetterPdfRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default);
}