using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities.Dto
{
    /// <summary>
    /// Request body for creating a new backend user account.
    /// </summary>
    public class CreateUserDto
    {
        /// <summary>
        /// Login name that should be reserved for the new user.
        /// </summary>
        public string Username { get; set; } = "";

        /// <summary>
        /// Raw password that will be hashed before persistence.
        /// </summary>
        public string Password { get; set; } = "";

        /// <summary>
        /// Email address stored with the created user account.
        /// </summary>
        public string Email { get; set; } = "";
    }
}