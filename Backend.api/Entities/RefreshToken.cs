using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class RefreshToken
    {
        private RefreshToken() {}
        public RefreshToken(User user, string ip, string token)
        {
            Id = Guid.NewGuid();
            this.User =user;
            this.Token = token;
            ExpiryDate = DateTime.UtcNow.AddDays(7);
            CreatedAt = DateTime.UtcNow;
            CreatedByIp = ip;
            IsRevoked = false;
        }
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public User User { get; set; }
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedByIp { get; set; } = string.Empty;
        public bool IsRevoked { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiryDate;
        public bool IsActive => !IsRevoked && !IsExpired;
    }
}