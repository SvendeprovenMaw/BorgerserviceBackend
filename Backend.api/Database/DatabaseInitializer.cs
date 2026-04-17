using Backend.api.Entities;
using Backend.api.Enums;
using JwtLibrary;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Backend.api.Database;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplyAIDbContext>();
        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInitializer");

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        catch (Exception ex) when (IsNonCriticalMigrationException(ex))
        {
            logger.LogWarning(ex, "Skipping database migration because schema is already present or model drift is expected in local development.");
        }

        await SeedUserAsync(context, configuration, cancellationToken);
    }

    private static bool IsNonCriticalMigrationException(Exception exception)
    {
        if (exception is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("PendingModelChangesWarning", StringComparison.Ordinal))
        {
            return true;
        }

        if (exception is PostgresException postgresException
            && postgresException.SqlState == PostgresErrorCodes.DuplicateTable)
        {
            return true;
        }

        return exception.InnerException is not null && IsNonCriticalMigrationException(exception.InnerException);
    }

    private static async Task SeedUserAsync(ApplyAIDbContext context, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var username = configuration["SeedUser:Username"];
        var email = configuration["SeedUser:Email"];
        var password = configuration["SeedUser:Password"];

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existingUser = await context.Users
            .AnyAsync(user => user.Username == username || user.Email == email, cancellationToken);

        if (existingUser)
        {
            return;
        }

        var role = Enum.TryParse<JwtRoles>(configuration["SeedUser:Role"], true, out var configuredRole)
            ? configuredRole
            : JwtRoles.User;

        var seededUser = new User(role, email, username, PasswordHasher.Hash(password, string.Empty));
        await context.Users.AddAsync(seededUser, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}