using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Applies conservative repairs to matching output before regeneration is attempted.
/// </summary>
public interface IMatchingDeterministicRepairService
{
    /// <summary>
    /// Repairs structurally suspicious matching records without inventing new evidence links.
    /// </summary>
    Task<MatchingRepairResult> RepairAsync(string matchingJson, CancellationToken cancellationToken = default);
}