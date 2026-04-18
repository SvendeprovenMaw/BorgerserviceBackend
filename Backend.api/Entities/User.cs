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
        public User(JwtRoles role, string Email, string Username, string Password, Guid? id = null)
        {
            this.Id = id ?? Guid.NewGuid();
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

        public void UpdateIdentity(string email, string username, JwtRoles role)
        {
            Email = email;
            Username = username;
            Role = role;
        }

        public void UpdatePassword(string password, string salt = "")
        {
            Password = password;
            Salt = salt;
        }
    }
}