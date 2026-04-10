using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

public interface IOpenAiResponsesService
{
    Task<string> GenerateStrictJsonAsync(StrictJsonResponseRequest request, CancellationToken cancellationToken = default);

    Task<string> GenerateStructuredJsonAsync(StructuredJsonResponseRequest request, CancellationToken cancellationToken = default);
}
