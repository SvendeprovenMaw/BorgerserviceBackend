using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services;

public interface IProfileService
{
    Task<ProfileDto> GetProfileAsync(User user, CancellationToken cancellationToken = default);
    Task<ProfileDto> UpdateProfileAsync(User user, ProfileDto profileDto, CancellationToken cancellationToken = default);
    Task<ProfilePreferencesDto> GetPreferencesAsync(User user, CancellationToken cancellationToken = default);
    Task<ProfilePreferencesDto> UpdatePreferencesAsync(User user, ProfilePreferencesDto preferencesDto, CancellationToken cancellationToken = default);
}

public class ProfileService : IProfileService
{
    private readonly ApplyAIDbContext _db;

    public ProfileService(ApplyAIDbContext db)
    {
        _db = db;
    }

    public async Task<ProfileDto> GetProfileAsync(User user, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(user, cancellationToken);
        return MapProfile(user, profile);
    }

    public async Task<ProfileDto> UpdateProfileAsync(User user, ProfileDto profileDto, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(user, cancellationToken);
        var nextApplicantId = profileDto.ApplicantId.Trim();

        if (!string.IsNullOrWhiteSpace(nextApplicantId))
        {
            var applicantIdInUse = await _db.Profiles
                .AnyAsync(item => item.UserId != user.Id && item.ApplicantId == nextApplicantId, cancellationToken);

            if (applicantIdInUse)
            {
                throw new InvalidOperationException($"Applicant id '{nextApplicantId}' is already in use.");
            }
        }

        user.Email = profileDto.Email.Trim().ToLowerInvariant();
        profile.UpdatePersonalDetails(
            applicantId: nextApplicantId,
            fullName: profileDto.Name,
            phoneNumber: profileDto.Phone,
            municipality: profileDto.Municipality,
            shortBio: profileDto.ShortBio,
            profileEnhancementJson: ProfileDefaults.SerializeProfileEnhancement(profileDto.ProfileEnhancement ?? ProfileDefaults.CreateDefaultEnhancement()));

        var synchronizedPreferences = ProfileDefaults.DeserializePreferences(
            profile.PreferencesJson,
            profile.ApplicantId,
            profile.FullName);
        synchronizedPreferences.ApplicantId = profile.ApplicantId;
        synchronizedPreferences.ApplicantDisplayName = profile.FullName;
        profile.UpdatePreferences(ProfileDefaults.SerializePreferences(synchronizedPreferences));

        await _db.SaveChangesAsync(cancellationToken);
        return MapProfile(user, profile);
    }

    public async Task<ProfilePreferencesDto> GetPreferencesAsync(User user, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(user, cancellationToken);
        return MapPreferences(profile);
    }

    public async Task<ProfilePreferencesDto> UpdatePreferencesAsync(User user, ProfilePreferencesDto preferencesDto, CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateProfileAsync(user, cancellationToken);
        var normalizedPreferences = ProfileDefaults.NormalizePreferences(
            preferencesDto,
            applicantId: profile.ApplicantId,
            applicantDisplayName: profile.FullName);

        profile.UpdatePreferences(ProfileDefaults.SerializePreferences(normalizedPreferences));
        await _db.SaveChangesAsync(cancellationToken);
        return MapPreferences(profile);
    }

    private async Task<Profile> GetOrCreateProfileAsync(User user, CancellationToken cancellationToken)
    {
        var profile = await _db.Profiles
            .Include(item => item.CurrentCv)
            .Include(item => item.RelevantDocuments)
            .FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);

        if (profile is not null)
        {
            return profile;
        }

        profile = new Profile(
            user,
            applicantId: user.Username,
            fullName: user.Username,
            preferencesJson: ProfileDefaults.SerializePreferences(ProfileDefaults.CreateDefaultPreferences(user.Username, user.Username)),
            profileEnhancementJson: ProfileDefaults.SerializeProfileEnhancement(ProfileDefaults.CreateDefaultEnhancement()));

        await _db.Profiles.AddAsync(profile, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    private static ProfileDto MapProfile(User user, Profile profile) => new()
    {
        Name = profile.FullName,
        ApplicantId = profile.ApplicantId,
        Email = user.Email,
        Phone = profile.PhoneNumber,
        Municipality = profile.Municipality,
        ShortBio = profile.ShortBio,
        ProfileEnhancement = ProfileDefaults.DeserializeProfileEnhancement(profile.ProfileEnhancementJson),
    };

    private static ProfilePreferencesDto MapPreferences(Profile profile)
        => ProfileDefaults.DeserializePreferences(profile.PreferencesJson, profile.ApplicantId, profile.FullName);
}