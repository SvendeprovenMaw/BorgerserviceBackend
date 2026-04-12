using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Repairs structurally broken requirements output when the model produced duplicate or missing IDs.
/// </summary>
public interface IRequirementsDeterministicRepairService
{
    /// <summary>
    /// Rewrites requirement identifiers conservatively so later stages can reference them reliably.
    /// </summary>
    Task<RequirementsRepairResult> RepairAsync(string requirementsJson, CancellationToken cancellationToken = default);
}