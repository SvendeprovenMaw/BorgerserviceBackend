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
    private const string FixtureSeedPassword = "ApplyAITest123!";
    private static readonly Guid ConfiguredSeedUserId = Guid.Parse("33333333-3333-3333-3333-333333333003");
    private static readonly Guid ConfiguredSeedCurrentCvFileId = Guid.Parse("33333333-3333-3333-3333-333333333103");

    private static readonly SeededCitizen[] FixtureCitizens =
    [
        new(
            DirectoryName: "Borger1",
            UserId: Guid.Parse("11111111-1111-1111-1111-111111111001"),
            ApplicantId: "fictional_citizen_01",
            CurrentCvFileId: Guid.Parse("11111111-1111-1111-1111-111111111101"),
            Email: "emma.sorensen@example.com",
            FullName: "Emma Sørensen",
            Phone: "+45 28 54 71 63",
            Municipality: "Roskilde",
            ShortBio: "Nysgerrig og løsningsorienteret junior softwareudvikler med fokus på moderne webudvikling, API-design og stabile løsninger i produktion."),
        new(
            DirectoryName: "Borger2",
            UserId: Guid.Parse("22222222-2222-2222-2222-222222222002"),
            ApplicantId: "fictional_citizen_02",
            CurrentCvFileId: Guid.Parse("22222222-2222-2222-2222-222222222202"),
            Email: "camilla.norgaard@example.com",
            FullName: "Camilla Nørgaard",
            Phone: "+45 31 77 40 92",
            Municipality: "Næstved",
            ShortBio: "Serviceminded og struktureret administrativ koordinator med erfaring inden for planlægning, sagsunderstøttelse og borgerkontakt."),
    ];

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
        await SeedConfiguredUserAsync(context, storageService, configuration, logger, cancellationToken);

        foreach (var citizen in FixtureCitizens)
        {
            await SeedFixtureCitizenAsync(context, storageService, citizen, logger, cancellationToken);
        }
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

        var configuredSeedTemplate = FixtureCitizens[0];
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

        var fixtureDirectory = ResolveFixtureDirectory(configuredSeedTemplate.DirectoryName);
        if (fixtureDirectory is null)
        {
            logger.LogWarning(
                "Configured seed user sample data could not be resolved from TestData/Borgere/{DirectoryName}. Falling back to default empty profile data.",
                configuredSeedTemplate.DirectoryName);

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
        var cvPath = Directory.GetFiles(fixtureDirectory, "*_cv_*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var profileDocumentPath = Path.Combine(fixtureDirectory, "profile.pdf");

        if (!File.Exists(preferencesPath) || !File.Exists(enhancementPath) || string.IsNullOrWhiteSpace(cvPath) || !File.Exists(profileDocumentPath))
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
            fullName: configuredSeedTemplate.FullName,
            phone: configuredSeedTemplate.Phone,
            municipality: configuredSeedTemplate.Municipality,
            shortBio: configuredSeedTemplate.ShortBio,
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

        var profileDocument = await EnsureUploadedFileAsync(
            context,
            storageService,
            seededUser,
            profileDocumentPath,
            FileCategory.CarreerDocument,
            null,
            cancellationToken);
        if (profileDocument is not null)
        {
            profile.AddRelevantDocument(profileDocument);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedFixtureCitizenAsync(ApplyAIDbContext context, IS3StorageService storageService, SeededCitizen citizen, ILogger logger, CancellationToken cancellationToken)
    {
        var testDataRoot = ResolveFixtureDirectory(citizen.DirectoryName);

        if (testDataRoot is null)
        {
            logger.LogWarning("Skipping fixture user {ApplicantId} because TestData/Borgere/{DirectoryName} could not be resolved from {BaseDirectory}.", citizen.ApplicantId, citizen.DirectoryName, AppContext.BaseDirectory);
            return;
        }

        var preferencesPath = Path.Combine(testDataRoot, "Preferences.json");
        var enhancementPath = Path.Combine(testDataRoot, "ProfileEnhancement.json");
        var cvPath = Directory.GetFiles(testDataRoot, "*_cv_*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
        var profileDocumentPath = Path.Combine(testDataRoot, "profile.pdf");

        if (!File.Exists(preferencesPath) || !File.Exists(enhancementPath) || string.IsNullOrWhiteSpace(cvPath) || !File.Exists(profileDocumentPath))
        {
            logger.LogWarning("Skipping fixture user {ApplicantId} because one or more seed files are missing under {SeedPath}.", citizen.ApplicantId, testDataRoot);
            return;
        }

        var user = await GetOrCreateUserAsync(
            context,
            citizen.ApplicantId,
            citizen.Email,
            FixtureSeedPassword,
            JwtRoles.User,
            citizen.UserId,
            cancellationToken);

        var preferencesJson = await File.ReadAllTextAsync(preferencesPath, cancellationToken);
        var enhancementJson = await File.ReadAllTextAsync(enhancementPath, cancellationToken);

        var profile = await EnsureProfileAsync(
            context,
            user,
            applicantId: citizen.ApplicantId,
            fullName: citizen.FullName,
            phone: citizen.Phone,
            municipality: citizen.Municipality,
            shortBio: citizen.ShortBio,
            preferencesJson: preferencesJson,
            profileEnhancementJson: enhancementJson,
            cancellationToken);

        var currentCv = await EnsureUploadedFileAsync(context, storageService, user, cvPath, FileCategory.Cv, citizen.CurrentCvFileId, cancellationToken);
        if (currentCv is not null)
        {
            profile.SetCurrentCv(currentCv);
        }

        var profileDocument = await EnsureUploadedFileAsync(context, storageService, user, profileDocumentPath, FileCategory.CarreerDocument, null, cancellationToken);
        if (profileDocument is not null)
        {
            profile.AddRelevantDocument(profileDocument);
        }

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
        var existingUser = await context.Users.FirstOrDefaultAsync(
            user => user.Username == username || user.Email == email,
            cancellationToken);

        if (existingUser is not null)
        {
            return existingUser;
        }

        var seededUser = new User(role, email.ToLowerInvariant(), username, PasswordHasher.Hash(password, string.Empty), userIdOverride);
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
        if (fileIdOverride.HasValue)
        {
            var existingById = await context.S3Files.FirstOrDefaultAsync(
                file => file.UserId == user.Id && file.Id == fileIdOverride.Value,
                cancellationToken);

            if (existingById is not null)
            {
                return existingById;
            }
        }

        var existingFile = await context.S3Files.FirstOrDefaultAsync(
            file => file.UserId == user.Id && file.FileName == filename,
            cancellationToken);

        if (existingFile is not null)
        {
            if (fileIdOverride.HasValue && existingFile.Id != fileIdOverride.Value)
            {
                await RealignSeededFileAsync(context, existingFile, cancellationToken);
            }
            else
            {
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
            var seededStorageKey = BuildSeededStorageKey(user.Id, fileCategory, fileIdOverride.Value, filename);
            if (await storageService.HasObjectTagAsync(seededStorageKey, "Seeded", "true", cancellationToken))
            {
                await storageService.RegisterExistingFileAsync(
                    seededStorageKey,
                    consentDto,
                    filename,
                    user,
                    await ComputeChecksumSha256Async(filePath, cancellationToken),
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

    private static Guid CreateDeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static string BuildSeededStorageKey(Guid userId, FileCategory fileCategory, Guid fileId, string filename)
    {
        return $"users/{userId}/{fileCategory}/seeded/{fileId:N}_{Path.GetFileName(filename)}";
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

    private sealed record SeededCitizen(
        string DirectoryName,
        Guid UserId,
        string ApplicantId,
        Guid CurrentCvFileId,
        string Email,
        string FullName,
        string Phone,
        string Municipality,
        string ShortBio);
}