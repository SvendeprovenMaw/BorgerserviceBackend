using Backend.api.Entities;
using Backend.api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Database;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();

        await context.Database.MigrateAsync(cancellationToken);
        await SeedUserAsync(context, configuration, cancellationToken);
    }

    private static async Task SeedUserAsync(WarehouseDbContext context, IConfiguration configuration, CancellationToken cancellationToken)
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

        var role = Enum.TryParse<UserRole>(configuration["SeedUser:Role"], true, out var configuredRole)
            ? configuredRole
            : UserRole.User;

        var seededUser = new User(role, email, username, PasswordHasher.Hash(password, string.Empty));
        await context.Users.AddAsync(seededUser, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }
}