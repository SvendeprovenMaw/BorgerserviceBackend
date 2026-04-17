using Microsoft.Extensions.Configuration;

namespace JwtLibrary.Configuration
{
    public static class JwtConfigurationReader
    {
        public static string GetEncryptionKey(IConfiguration configuration) =>
            GetRequiredValue(configuration, "Key", "Secret:Key");

        public static string GetSigningKey(IConfiguration configuration) =>
            GetRequiredValue(configuration, "JwtSettings:Key", "Secret:JwtSettings:Key");

        public static string GetIssuer(IConfiguration configuration) =>
            GetRequiredValue(configuration, "JwtSettings:Issuer", "Secret:JwtSettings:Issuer");

        public static string GetAudience(IConfiguration configuration) =>
            GetRequiredValue(configuration, "JwtSettings:Audience", "Secret:JwtSettings:Audience");

        public static string GetActor(IConfiguration configuration) =>
            GetValue(configuration, "JwtSettings:Actor", "Secret:JwtSettings:Actor") ?? string.Empty;

        public static int GetDurationInMinutes(IConfiguration configuration)
        {
            var configuredValue = GetValue(
                configuration,
                "JwtSettings:DurationInMinutes",
                "JwtSettings:Duration",
                "Secret:JwtSettings:DurationInMinutes",
                "Secret:JwtSettings:Duration");

            if (string.IsNullOrWhiteSpace(configuredValue) || !int.TryParse(configuredValue, out var durationInMinutes) || durationInMinutes <= 0)
            {
                throw new InvalidOperationException("Missing or invalid JWT duration configuration.");
            }

            return durationInMinutes;
        }

        private static string GetRequiredValue(IConfiguration configuration, params string[] keys)
        {
            var value = GetValue(configuration, keys);

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Missing configuration value. Checked: {string.Join(", ", keys)}.");
            }

            return value;
        }

        private static string? GetValue(IConfiguration configuration, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = configuration[key];

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }
    }
}