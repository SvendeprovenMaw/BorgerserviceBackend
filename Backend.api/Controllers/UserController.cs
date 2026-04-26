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
        [HttpPost]
        public async Task<IActionResult> RegisterUser(CreateUserDto createUserDto)
        {
            if(await _UserService.CreateUser(createUserDto))
            {
                return Created();
            }

            return NoContent();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto loginDto)
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

        [HttpPost("acceptterms")]
        public async Task<IActionResult> AcceptTerms()
        {
            return NotFound();
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteUser()
        {
            var user = await this._UserService.GetUser(HttpContext.User);
            if(user == null)
            {
                return NotFound("User not found");
            }
            await _UserService.HardDeleteAccount(user);
            return NotFound();
        }

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