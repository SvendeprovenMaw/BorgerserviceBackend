using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Backend.api.Configuration;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Backend.api.Controllers
{
    /// <summary>
    /// User-account routes for registration, login, and token refresh.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly JwtSettings jwtSettings;
        private readonly IUserService _UserService;
        private readonly IAuthService _authService;
        public UserController(IUserService userService, IOptions<JwtSettings> options, IAuthService authService)
        {
            this._UserService = userService;
            this.jwtSettings = options.Value;
            this._authService = authService;
        }

        /// <summary>
        /// Creates a new user account.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **username**: the login name that should be reserved for the new user.
        /// - **password**: the raw password that will be hashed and stored for the new account.
        /// - **email**: the email address stored with the user account.
        /// </remarks>
        /// <param name="createUserDto">Registration payload for the new user account.</param>
        /// <returns>A created response when registration succeeds, otherwise a no-content result.</returns>
        [HttpPost]
        public async Task<IActionResult> RegisterUser([FromBody] CreateUserDto createUserDto)
        {
            if(await _UserService.CreateUser(createUserDto))
            {
                return Created();
            }

            return NoContent();
        }

        /// <summary>
        /// Signs a user in and issues the access-token and refresh-token cookies.
        /// </summary>
        /// <remarks>
        /// Input fields:
        /// - **email**: the email address used to locate the user account.
        /// - **password**: the raw password used to verify the stored password hash.
        ///
        /// On success the route sets:
        /// - the `AccessToken` cookie used by authorized backend routes
        /// - the `RefreshToken` cookie used by the refresh route
        /// </remarks>
        /// <param name="loginDto">Login payload containing email and password.</param>
        /// <returns>An OK result when login succeeds, or a not-found/no-content response when it does not.</returns>
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            try
            {
                var User = await _UserService.Login(loginDto);
                if(User == null)
                {
                    return NotFound();
                }

                if(User.Password == PasswordHasher.Hash(loginDto.Password, User.Salt))
                {
                    var cookieOptions = new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = false, // Only sends over HTTPS
                        SameSite = SameSiteMode.Strict,
                        Expires = DateTime.UtcNow.AddMinutes(jwtSettings.DurationInMinutes)
                    };

                    var claims = new List<Claim>()
                    {
                        new Claim(JwtRegisteredClaimNames.Sub, User.Id.ToString()),
                        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                        new Claim(ClaimTypes.Role, User.Role.ToString()),
                    };
                    var jwt = JwtLibrary.TokenGenerator.JwtGenerator.CreateToken(jwtSettings.Key, jwtSettings.Issuer, jwtSettings.Audience, jwtSettings.DurationInMinutes, claims);
                    string refreshTokenString = JwtLibrary.TokenGenerator.GenerateRefreshToken();
                    var refreshToken = new RefreshToken(User, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", refreshTokenString);
                    await this._authService.SaveRefreshToken(refreshToken);
                    Response.Cookies.Append("AccessToken", jwt, cookieOptions);

                    Response.Cookies.Append("RefreshToken", refreshTokenString, new CookieOptions {
                        HttpOnly = true, Secure = false, SameSite = SameSiteMode.Strict,
                        Expires = refreshToken.ExpiryDate,
                        Path = "/api/user/refresh"
                    });
                    return Ok(new { message = "login successful" });
                }

                return NoContent();
            }
            catch (System.Exception)
            {
                
                throw;
            }
        }

        /// <summary>
        /// Reserved delete route for future user-removal support.
        /// </summary>
        /// <remarks>
        /// The route is present in the controller but is not implemented yet and currently always returns `404 Not Found`.
        /// </remarks>
        /// <returns>A not-found response because deletion is not implemented yet.</returns>
        [HttpDelete]
        public async Task<IActionResult> DeleteUser()
        {
            return NotFound();
        }

        /// <summary>
        /// Exchanges a valid refresh-token cookie for a new access-token and refresh-token pair.
        /// </summary>
        /// <remarks>
        /// This route does not take a JSON body.
        ///
        /// Input source:
        /// - **RefreshToken cookie**: read from the incoming request and validated against the backend database.
        ///
        /// When the refresh token is valid, the route revokes the old token, creates a new token pair, and writes updated cookies back to the client.
        /// </remarks>
        /// <returns>An OK result when refresh succeeds, or an unauthorized response when the refresh token is missing, invalid, or expired.</returns>
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh()
        {
            // 1. Get the token from the cookie
            var refreshToken = Request.Cookies["RefreshToken"];

            if (string.IsNullOrEmpty(refreshToken))
                return Unauthorized("No refresh token provided.");

            // 2. Validate the token in the DB
            var tokenEntity = await _authService.GetRefreshToken(refreshToken);

            if (tokenEntity == null || !tokenEntity.IsActive)
                return Unauthorized("Token is invalid or expired.");

            // 3. (Optional but recommended) Rotate the token
            // Revoke the old one
            await _authService.RevokeToken(tokenEntity);

            // 4. Generate new pair
            // Re-load the user to get claims
            var user = tokenEntity.User; 
            var claims = new List<Claim>()
            {
                new Claim(JwtRegisteredClaimNames.Sub, tokenEntity.User.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, tokenEntity.User.Role.ToString()),
            };
            
            var newJwt = JwtLibrary.TokenGenerator.JwtGenerator.CreateToken(jwtSettings.Key, jwtSettings.Issuer, jwtSettings.Audience, jwtSettings.DurationInMinutes, claims);
            var newRefreshTokenString = JwtLibrary.TokenGenerator.GenerateRefreshToken();
            
            // Save new one to DB
            var newEntity = new RefreshToken(user, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1", newRefreshTokenString);
            await _authService.SaveRefreshToken(newEntity);

            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true, // Only sends over HTTPS
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddMinutes(jwtSettings.DurationInMinutes)
            };

            var refreshTokenOptions = new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddDays(7), // Usually matches your RefreshToken.ExpiryDate
                Path = "/api/user/refresh" // Higher security: browser only sends this to the refresh endpoint
            };
            // 5. Update Cookies
            Response.Cookies.Append("AccessToken", newJwt, cookieOptions);
            Response.Cookies.Append("RefreshToken", newRefreshTokenString, refreshTokenOptions);

            return Ok(new { message = "Token refreshed" });
        }
    }
}