using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface ICoverLetterTemplateRenderer
{
    Task<CoverLetterTemplateRenderResult> RenderAsync(string applicationJson, CancellationToken cancellationToken = default);
}