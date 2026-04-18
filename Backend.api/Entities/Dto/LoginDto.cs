using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    /// <summary>
    /// Request body for user login.
    /// </summary>
    public class LoginDto
    {
        /// <summary>
        /// Email used to locate the account.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Raw password used to verify the stored password hash.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}