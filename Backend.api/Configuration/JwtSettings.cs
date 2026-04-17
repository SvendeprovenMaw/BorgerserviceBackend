using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Configuration
{
    public class JwtSettings
    {
        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public string Actor { get; set; } = string.Empty;
        public int DurationInMinutes { get; set; }
        public int Duration
        {
            get => DurationInMinutes;
            set => DurationInMinutes = value;
        }
    }
}