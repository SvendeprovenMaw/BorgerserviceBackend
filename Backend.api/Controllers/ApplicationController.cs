using Backend.api.Entities.Dto;
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
    /// <summary>
    /// Returns the current user's sent applications.
    /// </summary>
    /// <remarks>
    /// The documents page currently uses this route to remove the frontend-side placeholder error state.
    ///
    /// No sent-application persistence is wired yet, so the backend returns an empty list until that storage layer exists.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationSummaryDto>), StatusCodes.Status200OK)]
    public IActionResult ListApplications()
    {
        return Ok(Array.Empty<ApplicationSummaryDto>());
    }

    /// <summary>
    /// Returns one sent application when the backing store exists.
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApplicationSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetApplication(string id)
    {
        return NotFound(new { message = $"Application '{id}' was not found." });
    }
}