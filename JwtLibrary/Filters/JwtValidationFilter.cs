using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using JwtLibrary.Configuration;

namespace JwtLibrary.Filters
{
    public class JwtValidationFilter : Attribute, IAsyncAuthorizationFilter
    {
        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            try
            {
                IConfiguration config = (context.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration)!;
                string encryptionKey = JwtConfigurationReader.GetEncryptionKey(config);
                string issuer = JwtConfigurationReader.GetIssuer(config);
                string audience = JwtConfigurationReader.GetAudience(config);
                string signingKey = JwtConfigurationReader.GetSigningKey(config);
                string EncryptedToken = context.HttpContext.Request.Headers.First(x => x.Key == "Authorization").Value!;
                EncryptedToken = EncryptedToken.Replace("Bearer ", string.Empty);
                EncryptedToken = EncryptedToken.Replace("\"", string.Empty);
                var token = Encrypter.Decrypt(EncryptedToken, encryptionKey);
                
                JsonWebTokenHandler handler = new JsonWebTokenHandler();
                TokenValidationParameters parameters = new TokenValidationParameters
                {
                    ValidIssuer = issuer,
                    ValidAudience = audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidateActor = false,
                };

                var result = await handler.ValidateTokenAsync(token, parameters);
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