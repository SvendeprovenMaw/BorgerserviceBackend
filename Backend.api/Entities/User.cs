using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.api.Enums;
using JwtLibrary;

namespace Backend.api.Entities
{
    public class User
    {
        protected User() {}
        public User(JwtRoles role, string Email, string Username, string Password)
        {
            this.Id = Guid.NewGuid();
            this.Role = role;
            this.Password = Password;
            this.Username = Username;
            this.Email = Email;
            
        }
        public Guid Id { get; private set; }
        public JwtRoles Role { get; private set; }
        public string Email { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public string Salt { get; set; } = string.Empty;

        public void AnonymizeUser()
        {
            this.Role = JwtRoles.NonApprovedGuest;
            this.Email = "anonymized ";
            this.Username = "anonymized ";
            this.Password = "anonymized ";
            this.Salt = "anonymized ";
        }
        
    }
}