using Backend.api.Entities.Dto;
using Backend.api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.api.Controllers;

/// <summary>
/// Authenticated routes for the current user's personal profile and application preferences.
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IProfileService _profileService;

    public ProfileController(IUserService userService, IProfileService profileService)
    {
        _userService = userService;
        _profileService = profileService;
    }

    /// <summary>
    /// Returns the authenticated user's editable profile details.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var user = await _userService.GetUser(HttpContext.User);
        var profile = await _profileService.GetProfileAsync(user, cancellationToken);
        return Ok(profile);
    }

    /// <summary>
    /// Updates the authenticated user's editable profile details.
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<ProfileDto>> UpdateProfile([FromBody] ProfileDto profileDto, CancellationToken cancellationToken)
    {
        var user = await _userService.GetUser(HttpContext.User);

        try
        {
            var updatedProfile = await _profileService.UpdateProfileAsync(user, profileDto, cancellationToken);
            return Ok(updatedProfile);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Returns the authenticated user's application-generation preferences.
    /// </summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<ProfilePreferencesDto>> GetPreferences(CancellationToken cancellationToken)
    {
        var user = await _userService.GetUser(HttpContext.User);
        var preferences = await _profileService.GetPreferencesAsync(user, cancellationToken);
        return Ok(preferences);
    }

    /// <summary>
    /// Updates the authenticated user's application-generation preferences.
    /// </summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<ProfilePreferencesDto>> UpdatePreferences([FromBody] ProfilePreferencesDto preferencesDto, CancellationToken cancellationToken)
    {
        var user = await _userService.GetUser(HttpContext.User);
        var updatedPreferences = await _profileService.UpdatePreferencesAsync(user, preferencesDto, cancellationToken);
        return Ok(updatedPreferences);
    }
}