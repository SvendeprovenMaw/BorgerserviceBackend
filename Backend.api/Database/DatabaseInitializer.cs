using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Backend.api.Services;
using JwtLibrary;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

namespace Backend.api.Database;

public static class DatabaseInitializer
{
    private const string SeededProfileDocumentFileName = "profile.pdf";
    private const string ConfiguredSeedFixtureDirectoryName = "Borger1";
    private const string ConfiguredSeedCvFileName = "emma_sorensen_cv_da.pdf";
    private static readonly Guid ConfiguredSeedUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfiguredSeedCurrentCvFileId = Guid.Parse("33333333-3333-3333-3333-333333333103");
    private static readonly Guid[] LegacyFixtureUserIds =
    [
        Guid.Parse("11111111-1111-1111-1111-111111111001"),
        Guid.Parse("22222222-2222-2222-2222-222222222002"),
    ];
    private static readonly SeededProfileTemplate ConfiguredSeedTemplate = new(
        DirectoryName: ConfiguredSeedFixtureDirectoryName,
        CvFileName: ConfiguredSeedCvFileName,
        FullName: "Emma Sørensen",
        Phone: "+45 28 54 71 63",
        Municipality: "Roskilde",
        ShortBio: "Nysgerrig og løsningsorienteret junior softwareudvikler med fokus på moderne webudvikling, API-design og stabile løsninger i produktion.");

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplyAIDbContext>();
        var storageService = scope.ServiceProvider.GetRequiredService<IS3StorageService>();
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

        await SeedUsersAsync(context, storageService, configuration, logger, cancellationToken);
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

    private static async Task SeedUsersAsync(ApplyAIDbContext context, IS3StorageService storageService, IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        await RemoveLegacyFixtureUsersAsync(context, cancellationToken);
        await SeedConfiguredUserAsync(context, storageService, configuration, logger, cancellationToken);
    }

    private static async Task RemoveLegacyFixtureUsersAsync(ApplyAIDbContext context, CancellationToken cancellationToken)
    {
        await context.Users
            .Where(user => LegacyFixtureUserIds.Contains(user.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static async Task SeedConfiguredUserAsync(
        ApplyAIDbContext context,
        IS3StorageService storageService,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var username = configuration["SeedUser:Username"]?.Trim();
        var email = configuration["SeedUser:Email"]?.Trim().ToLowerInvariant();
        var password = configuration["SeedUser:Password"];

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var role = Enum.TryParse<JwtRoles>(configuration["SeedUser:Role"], true, out var configuredRole)
            ? configuredRole
            : JwtRoles.User;

        var seededUser = await GetOrCreateUserAsync(
            context,
            username,
            email,
            password,
            role,
            ConfiguredSeedUserId,
            cancellationToken);

        seededUser.UpdateIdentity(email, username, role);
        seededUser.UpdatePassword(PasswordHasher.Hash(password, string.Empty));

        var fixtureDirectory = ResolveFixtureDirectory(ConfiguredSeedTemplate.DirectoryName);
        if (fixtureDirectory is null)
        {
            logger.LogWarning(
                "Configured seed user sample data could not be resolved from TestData/Borgere/{DirectoryName}. Falling back to default empty profile data.",
            ConfiguredSeedTemplate.DirectoryName);

            await EnsureProfileAsync(
                context,
                seededUser,
                applicantId: username,
                fullName: username,
                phone: string.Empty,
                municipality: string.Empty,
                shortBio: string.Empty,
                preferencesJson: ProfileDefaults.SerializePreferences(ProfileDefaults.CreateDefaultPreferences(username, username)),
                profileEnhancementJson: ProfileDefaults.SerializeProfileEnhancement(ProfileDefaults.CreateDefaultEnhancement()),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var preferencesPath = Path.Combine(fixtureDirectory, "Preferences.json");
        var enhancementPath = Path.Combine(fixtureDirectory, "ProfileEnhancement.json");
        var cvPath = Path.Combine(fixtureDirectory, ConfiguredSeedTemplate.CvFileName);
        if (!File.Exists(preferencesPath) || !File.Exists(enhancementPath) || !File.Exists(cvPath))
        {
            logger.LogWarning(
                "Configured seed user sample files are incomplete under {FixtureDirectory}. Falling back to default empty profile data.",
                fixtureDirectory);

            await EnsureProfileAsync(
                context,
                seededUser,
                applicantId: username,
                fullName: username,
                phone: string.Empty,
                municipality: string.Empty,
                shortBio: string.Empty,
                preferencesJson: ProfileDefaults.SerializePreferences(ProfileDefaults.CreateDefaultPreferences(username, username)),
                profileEnhancementJson: ProfileDefaults.SerializeProfileEnhancement(ProfileDefaults.CreateDefaultEnhancement()),
                cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var preferencesJson = await File.ReadAllTextAsync(preferencesPath, cancellationToken);
        var enhancementJson = await File.ReadAllTextAsync(enhancementPath, cancellationToken);

        var profile = await EnsureProfileAsync(
            context,
            seededUser,
            applicantId: username,
            fullName: ConfiguredSeedTemplate.FullName,
            phone: ConfiguredSeedTemplate.Phone,
            municipality: ConfiguredSeedTemplate.Municipality,
            shortBio: ConfiguredSeedTemplate.ShortBio,
            preferencesJson: preferencesJson,
            profileEnhancementJson: enhancementJson,
            cancellationToken);

        var currentCv = await EnsureUploadedFileAsync(
            context,
            storageService,
            seededUser,
            cvPath,
            FileCategory.Cv,
            ConfiguredSeedCurrentCvFileId,
            cancellationToken);
        if (currentCv is not null)
        {
            profile.SetCurrentCv(currentCv);
        }

        await RemoveSeededFileIfPresentAsync(context, seededUser, SeededProfileDocumentFileName, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? ResolveFixtureDirectory(string directoryName)
    {
        var candidatePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestData", "Borgere", directoryName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "TestData", "Borgere", directoryName)),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "TestData", "Borgere", directoryName)),
        };

        return candidatePaths.FirstOrDefault(Directory.Exists);
    }

    private static async Task<User> GetOrCreateUserAsync(
        ApplyAIDbContext context,
        string username,
        string email,
        string password,
        JwtRoles role,
        Guid? userIdOverride,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        if (userIdOverride.HasValue)
        {
            var existingById = await context.Users.FirstOrDefaultAsync(
                user => user.Id == userIdOverride.Value,
                cancellationToken);

            if (existingById is not null)
            {
                return existingById;
            }
        }

        var existingUser = await context.Users.FirstOrDefaultAsync(
            user => user.Username == username || user.Email == normalizedEmail,
            cancellationToken);

        if (existingUser is not null)
        {
            if (userIdOverride.HasValue && existingUser.Id != userIdOverride.Value)
            {
                await RealignUserIdAsync(context, existingUser, userIdOverride.Value, cancellationToken);
                return await context.Users.FirstAsync(user => user.Id == userIdOverride.Value, cancellationToken);
            }

            return existingUser;
        }

        var seededUser = new User(role, normalizedEmail, username, PasswordHasher.Hash(password, string.Empty), userIdOverride);
        await context.Users.AddAsync(seededUser, cancellationToken);
        return seededUser;
    }

    private static async Task<Profile> EnsureProfileAsync(
        ApplyAIDbContext context,
        User user,
        string applicantId,
        string fullName,
        string phone,
        string municipality,
        string shortBio,
        string preferencesJson,
        string profileEnhancementJson,
        CancellationToken cancellationToken)
    {
        var profile = await context.Profiles
            .Include(item => item.CurrentCv)
            .Include(item => item.RelevantDocuments)
            .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (profile is null)
        {
            profile = new Profile(user, applicantId, fullName, preferencesJson, profileEnhancementJson);
            await context.Profiles.AddAsync(profile, cancellationToken);
        }

        var enhancement = ProfileDefaults.DeserializeProfileEnhancement(profileEnhancementJson);
        profile.UpdatePersonalDetails(applicantId, fullName, phone, municipality, shortBio, ProfileDefaults.SerializeProfileEnhancement(enhancement));
        profile.UpdatePreferences(ProfileDefaults.SerializePreferences(ProfileDefaults.DeserializePreferences(preferencesJson, applicantId, fullName)));
        return profile;
    }

    private static async Task<S3File?> EnsureUploadedFileAsync(
        ApplyAIDbContext context,
        IS3StorageService storageService,
        User user,
        string filePath,
        FileCategory fileCategory,
        Guid? fileIdOverride,
        CancellationToken cancellationToken)
    {
        var filename = Path.GetFileName(filePath);
        var seededStorageKey = fileCategory == FileCategory.Cv && fileIdOverride.HasValue
            ? BuildSeededStorageKey(user.Id, fileCategory, fileIdOverride.Value, filename)
            : null;

        if (fileIdOverride.HasValue)
        {
            var existingById = await context.S3Files.FirstOrDefaultAsync(
                file => file.UserId == user.Id && file.Id == fileIdOverride.Value,
                cancellationToken);

            if (existingById is not null)
            {
                if (await IsExistingFileRegistrationValidAsync(storageService, existingById, filename, seededStorageKey, cancellationToken))
                {
                    await EnsureFileConsentAsync(context, user, existingById, cancellationToken);
                    return existingById;
                }

                await RealignSeededFileAsync(context, existingById, cancellationToken);
            }
        }

        var existingFile = await context.S3Files.FirstOrDefaultAsync(
            file => file.UserId == user.Id && file.FileName == filename,
            cancellationToken);

        if (existingFile is not null)
        {
            var canReuseExistingFile = (!fileIdOverride.HasValue || existingFile.Id == fileIdOverride.Value)
                && await IsExistingFileRegistrationValidAsync(storageService, existingFile, filename, seededStorageKey, cancellationToken);

            if (canReuseExistingFile)
            {
                await EnsureFileConsentAsync(context, user, existingFile, cancellationToken);
                return existingFile;
            }

            if (fileIdOverride.HasValue || !await storageService.ObjectExistsAsync(existingFile.S3Key, cancellationToken))
            {
                await RealignSeededFileAsync(context, existingFile, cancellationToken);
            }
            else
            {
                await EnsureFileConsentAsync(context, user, existingFile, cancellationToken);
                return existingFile;
            }
        }

        existingFile = await context.S3Files.FirstOrDefaultAsync(
            file => file.UserId == user.Id && file.FileName == filename,
            cancellationToken);

        if (existingFile is not null)
        {
            return existingFile;
        }

        var consentDto = new GiveConsentDto
        {
            ConsentGiven = true,
            TimeOfConsent = DateTime.UtcNow,
        };

        if (fileCategory == FileCategory.Cv && fileIdOverride.HasValue)
        {
            var checksumHash = await ComputeChecksumSha256Async(filePath, cancellationToken);
            var existingStorageKey = await ResolveExistingSeededStorageKeyAsync(
                storageService,
                user.Id,
                fileCategory,
                filename,
                seededStorageKey!,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(existingStorageKey))
            {
                await storageService.RegisterExistingFileAsync(
                    existingStorageKey,
                    consentDto,
                    filename,
                    user,
                    checksumHash,
                    fileIdOverride,
                    DateTime.UtcNow);
            }
            else
            {
                await using var seededStream = File.OpenRead(filePath);
                await storageService.UploadFile(
                    seededStream,
                    consentDto,
                    filename,
                    user,
                    fileCategory,
                    fileIdOverride,
                    DateTime.UtcNow,
                    seededStorageKey,
                    objectTags: null,
                    "application/pdf");
            }
        }
        else
        {
            await using var stream = File.OpenRead(filePath);
            await storageService.UploadFile(
                stream,
                consentDto,
                filename,
                user,
                fileCategory,
                fileIdOverride,
                DateTime.UtcNow);
        }

        return await context.S3Files.FirstOrDefaultAsync(
            file => file.UserId == user.Id && file.FileName == filename,
            cancellationToken);
    }

    private static async Task<string?> ResolveExistingSeededStorageKeyAsync(
        IS3StorageService storageService,
        Guid userId,
        FileCategory fileCategory,
        string filename,
        string seededStorageKey,
        CancellationToken cancellationToken)
    {
        var userStoragePrefix = BuildUserStoragePrefix(userId, fileCategory);
        if (!await storageService.PrefixExistsAsync(userStoragePrefix, cancellationToken))
        {
            return null;
        }

        if (await storageService.ObjectExistsAsync(seededStorageKey, cancellationToken))
        {
            return seededStorageKey;
        }

        return await storageService.FindObjectKeyByFileNameAsync(userStoragePrefix, filename, cancellationToken);
    }

    private static async Task EnsureFileConsentAsync(
        ApplyAIDbContext context,
        User user,
        S3File file,
        CancellationToken cancellationToken)
    {
        var hasActiveConsent = await context.Consents.AnyAsync(
            consent => consent.FileId == file.Id && consent.UserId == user.Id && !consent.ConsentRetracted,
            cancellationToken);

        if (hasActiveConsent)
        {
            return;
        }

        var existingConsent = await context.Consents.FirstOrDefaultAsync(
            consent => consent.FileId == file.Id && consent.UserId == user.Id,
            cancellationToken);

        if (existingConsent is not null)
        {
            await context.Consents
                .Where(consent => consent.Id == existingConsent.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(consent => consent.ConsentRetracted, false)
                        .SetProperty(consent => consent.TimeOfConsent, DateTime.UtcNow),
                    cancellationToken);
            return;
        }

        await context.Consents.AddAsync(new Consent(user, file, DateTime.UtcNow), cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> IsExistingFileRegistrationValidAsync(
        IS3StorageService storageService,
        S3File existingFile,
        string expectedFilename,
        string? expectedStorageKey,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(existingFile.FileName, expectedFilename, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expectedStorageKey)
            && !string.Equals(existingFile.S3Key, expectedStorageKey, StringComparison.Ordinal))
        {
            return false;
        }

        return await storageService.ObjectExistsAsync(existingFile.S3Key, cancellationToken);
    }

    private static async Task RemoveSeededFileIfPresentAsync(
        ApplyAIDbContext context,
        User user,
        string filename,
        CancellationToken cancellationToken)
    {
        var existingFiles = await context.S3Files
            .Where(file => file.UserId == user.Id && file.FileName == filename)
            .ToListAsync(cancellationToken);

        foreach (var existingFile in existingFiles)
        {
            await RealignSeededFileAsync(context, existingFile, cancellationToken);
        }
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static string BuildSeededStorageKey(Guid userId, FileCategory fileCategory, Guid fileId, string filename)
    {
        return StoragePathBuilder.BuildUserDocumentStorageKey(userId, fileCategory, filename, fileId);
    }

    private static string BuildUserStoragePrefix(Guid userId, FileCategory fileCategory)
    {
        return StoragePathBuilder.BuildUserDocumentPrefix(userId, fileCategory);
    }

    private static async Task RealignUserIdAsync(
        ApplyAIDbContext context,
        User existingUser,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        if (existingUser.Id == targetUserId)
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var replacementUser = new User(existingUser.Role, existingUser.Email, existingUser.Username, existingUser.Password, targetUserId)
        {
            Salt = existingUser.Salt,
        };

        await context.Users.AddAsync(replacementUser, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        await context.Profiles
            .Where(profile => profile.UserId == existingUser.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(profile => profile.UserId, targetUserId), cancellationToken);

        await context.S3Files
            .Where(file => file.UserId == existingUser.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(file => file.UserId, targetUserId), cancellationToken);

        await context.Consents
            .Where(consent => consent.UserId == existingUser.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(consent => consent.UserId, targetUserId), cancellationToken);

        await context.RefreshTokens
            .Where(refreshToken => refreshToken.UserId == existingUser.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(refreshToken => refreshToken.UserId, targetUserId), cancellationToken);

        await context.ApplyAiPipelineJobs
            .Where(job => job.UserId == existingUser.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.UserId, targetUserId), cancellationToken);

        context.Users.Remove(existingUser);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    private static async Task<string> ComputeChecksumSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToBase64String(hash);
    }

    private static async Task RealignSeededFileAsync(
        ApplyAIDbContext context,
        S3File existingFile,
        CancellationToken cancellationToken)
    {
        var profiles = await context.Profiles
            .Include(profile => profile.RelevantDocuments)
            .Where(profile => profile.CurrentCvId == existingFile.Id || profile.RelevantDocuments.Any(document => document.Id == existingFile.Id))
            .ToListAsync(cancellationToken);

        foreach (var profile in profiles)
        {
            if (profile.CurrentCvId == existingFile.Id)
            {
                profile.SetCurrentCv(null);
            }

            var attachedDocument = profile.RelevantDocuments.FirstOrDefault(document => document.Id == existingFile.Id);
            if (attachedDocument is not null)
            {
                profile.RelevantDocuments.Remove(attachedDocument);
            }
        }

        var consent = await context.Consents.FirstOrDefaultAsync(item => item.FileId == existingFile.Id, cancellationToken);
        if (consent is not null)
        {
            context.Consents.Remove(consent);
        }

        context.S3Files.Remove(existingFile);
        await context.SaveChangesAsync(cancellationToken);
    }
    private sealed record SeededProfileTemplate(
        string DirectoryName,
        string CvFileName,
        string FullName,
        string Phone,
        string Municipality,
        string ShortBio);
}