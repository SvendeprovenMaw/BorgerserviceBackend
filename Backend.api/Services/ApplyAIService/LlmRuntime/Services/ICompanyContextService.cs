using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface ICompanyContextService
{
    Task<StructuredJsonGenerationResult> GenerateCompanyContextAsync(CompanyContextGenerationRequest request, CancellationToken cancellationToken = default);
}