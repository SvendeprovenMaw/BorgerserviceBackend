using Backend.api.Services.ApplyAIService.LlmRuntime.Models;

namespace Backend.api.Services.ApplyAIService.LlmRuntime.Services;

public interface IOpenAiResponsesService
{
    Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default);

    Task<StructuredJsonGenerationResult> GenerateStrictJsonWithMetadataAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default);

    Task<string> GenerateStructuredJsonAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default);

    Task<StructuredJsonGenerationResult> GenerateStructuredJsonWithMetadataAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default);
}