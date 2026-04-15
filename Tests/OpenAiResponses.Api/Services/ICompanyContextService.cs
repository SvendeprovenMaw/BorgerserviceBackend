using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Generates structured company-context research from a job posting, applicant profile data, and web search.
/// </summary>
public interface ICompanyContextService
{
    /// <summary>
    /// Generates structured company-context JSON plus response metadata.
    /// </summary>
    Task<StructuredJsonGenerationResult> GenerateCompanyContextAsync(CompanyContextGenerationRequest request, CancellationToken cancellationToken = default);
}