using Backend.api.Entities.Dto;
using Backend.api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers;

/// <summary>
/// Read-only application routes used by the documents page.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ApplicationController : ControllerBase
{
    private readonly ISentApplicationService _sentApplicationService;

    public ApplicationController(ISentApplicationService sentApplicationService)
    {
        _sentApplicationService = sentApplicationService;
    }

    /// <summary>
    /// Returns the current user's sent applications.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListApplications(CancellationToken cancellationToken)
    {
        return Ok(await _sentApplicationService.ListAsync(User, cancellationToken));
    }

    /// <summary>
    /// Saves the locked sent-application snapshot created by the Finish action in the live workflow.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveFinishedApplication(
        [FromBody] FinishedApplicationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _sentApplicationService.SaveAsync(User, request, cancellationToken);

        return result.Error switch
        {
            SaveFinishedApplicationError.None => Ok(result.Application),
            SaveFinishedApplicationError.JobNotFound => NotFound(new { message = result.Message }),
            _ => BadRequest(new { message = result.Message }),
        };
    }

    /// <summary>
    /// Returns one sent application review snapshot for the current user.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApplicationDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetApplication(Guid id, CancellationToken cancellationToken)
    {
        var application = await _sentApplicationService.GetAsync(User, id, cancellationToken);
        if (application is null)
        {
            return NotFound(new { message = $"Application '{id}' was not found." });
        }

        return Ok(application);
    }
}