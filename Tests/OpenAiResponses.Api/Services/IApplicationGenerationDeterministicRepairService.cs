using OpenAiResponses.Api.Models;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Repairs internal references inside the generated application document without changing its overall message.
/// </summary>
public interface IApplicationGenerationDeterministicRepairService
{
    /// <summary>
    /// Reconciles claims, sections, evidence links, and requirement links against verified upstream documents.
    /// </summary>
    Task<ApplicationGenerationRepairResult> RepairAsync(
        string applicationJson,
        string requirementsJson,
        string candidateEvidenceJson,
        CancellationToken cancellationToken = default);
}