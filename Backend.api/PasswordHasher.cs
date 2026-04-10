using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Backend.api
{
    public static class PasswordHasher
    {
        public static string Hash(string password, string salt)
        {
            string hashed = Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: Encoding.UTF8.GetBytes(salt),
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: 100000, // High number for security
                numBytesRequested: 256 / 8 // 32 bytes for 256-bit hash
            ));
            return hashed;
        }
    }
}