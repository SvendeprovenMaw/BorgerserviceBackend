using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace JwtLibrary.Filters
{
    public class JwtRoleValidationFilter : Attribute, IAsyncAuthorizationFilter
    {
        JwtRoles[] allowedRoles;
        public JwtRoleValidationFilter(params JwtRoles[] roles)
        {
            this.allowedRoles = roles;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            try
            {
                IConfiguration config = (context.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration)!;
                string EncryptedToken = context.HttpContext.Request.Headers.First(x => x.Key == "Authorization").Value!;
                EncryptedToken = EncryptedToken.Replace("Bearer ", string.Empty);
                EncryptedToken = EncryptedToken.Replace("\"", string.Empty);
                var token = Encrypter.Decrypt(EncryptedToken, config!["Secret:Key"]!);
                
                JsonWebTokenHandler handler = new JsonWebTokenHandler();
                TokenValidationParameters parameters = new TokenValidationParameters
                {
                    ValidIssuer = config!["JwtSettings:Issuer"]!,
                    ValidAudience = config!["JwtSettings:Audience"]!,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["JwtSettings:Key"]!)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidateActor = false,
                };

                var result = await handler.ValidateTokenAsync(token, parameters);
                string claimRole = (string)result.Claims.Where(i => i.Key == JwtRegisteredClaimNames.Aud).First().Value;
                if(!allowedRoles.Contains(Enum.Parse<JwtRoles>(claimRole))) { context.Result = new UnauthorizedResult(); return; }
                if(result.IsValid) { return; }

                context.Result = new StatusCodeResult(403);
                return;
            }
            catch
            {
                context.Result = new NoContentResult();
                return;
            }
        }
    }
}