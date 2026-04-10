using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace JwtLibrary
{
    public interface IJwtUser
    {
        public Guid Id { get; }
        public string Username { get; }
        public JwtRoles Role { get; }
    }
}