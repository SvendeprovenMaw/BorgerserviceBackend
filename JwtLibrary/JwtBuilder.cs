using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;
using System.Security.Cryptography;

namespace JwtLibrary
{
    public static class JwtBuilder
    {
        public static string GenerateJsonWebToken(string jwtKey, IJwtUser user, string issuer, string audience)
        {
            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("username", user.Username),
                new Claim(JwtRegisteredClaimNames.Aud, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, user.Id.ToString())
            };

            var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.Now.AddMinutes(30), signingCredentials: credentials);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public static byte[] GenerateJsonWebEncryption(string jwtKey, IJwtUser user, string issuer, string audience, string securityKey)
        {
            string plainText = GenerateJsonWebToken(jwtKey, user, issuer, audience);
            using (Aes cryp = Aes.Create())
            {
                cryp.Key = Encoding.UTF8.GetBytes(securityKey);
                cryp.IV = new byte[16];
                cryp.Mode = CipherMode.ECB;
                cryp.Padding = PaddingMode.PKCS7;
                using (var encrypter = cryp.CreateEncryptor())
                {
                    var encrypted = encrypter.TransformFinalBlock(Encoding.UTF8.GetBytes(plainText), 0, plainText.Length);
                    return encrypted;
                }
            }
        }
    }
}