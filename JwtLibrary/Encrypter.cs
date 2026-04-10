using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace JwtLibrary
{
    public static class Encrypter
    {
        public static byte[] Encrypt(string plainText, string securityKey)
        {
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


        public static string Decrypt(byte[] cipher, string securityKey)
        {
            using (Aes cryp = Aes.Create())
            {
                cryp.Key = Encoding.UTF8.GetBytes(securityKey);
                cryp.IV = new byte[16];
                cryp.Mode = CipherMode.ECB;
                cryp.Padding = PaddingMode.PKCS7;
                using (var decrypter = cryp.CreateDecryptor())
                {
                    var decryptedText = decrypter.TransformFinalBlock(cipher, 0, cipher.Length);
                    return Encoding.UTF8.GetString(decryptedText);
                }
            }
        }
        public static string Decrypt(string cipher, string securityKey)
        {
            var cipherBytes = Encoding.UTF8.GetBytes(cipher);
            return Decrypt(cipherBytes, securityKey);
        }
    }
}