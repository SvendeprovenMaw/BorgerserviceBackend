using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class Profile
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private Profile(){}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        public Profile(User user, string applicantId, string fullName, string preferencesJson, string profileEnhancementJson)
        {
            this.Id = Guid.NewGuid();
            this.User = user;
            this.UserId = user.Id;
            this.ApplicantId = applicantId;
            this.FullName = fullName;
            this.PreferencesJson = preferencesJson;
            this.ProfileEnhancementJson = profileEnhancementJson;
            this.RelevantDocuments = [];
        }
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; } = null!;
        public string ApplicantId { get; private set; } = string.Empty;
        public string FullName { get; private set; } = string.Empty;
        public string PhoneNumber { get; private set; } = string.Empty;
        public string Municipality { get; private set; } = string.Empty;
        public string ShortBio { get; private set; } = string.Empty;
        public string ProfileEnhancementJson { get; private set; } = "{}";
        public string PreferencesJson { get; private set; } = "{}";
        public Guid? CurrentCvId { get; private set; }
        public S3File? CurrentCv { get; private set; }
        public Collection<S3File> RelevantDocuments { get; private set; } = [];

        public void UpdatePersonalDetails(string applicantId, string fullName, string phoneNumber, string municipality, string shortBio, string profileEnhancementJson)
        {
            ApplicantId = string.IsNullOrWhiteSpace(applicantId) ? ApplicantId : applicantId.Trim();
            FullName = string.IsNullOrWhiteSpace(fullName) ? FullName : fullName.Trim();
            PhoneNumber = phoneNumber.Trim();
            Municipality = municipality.Trim();
            ShortBio = shortBio.Trim();
            ProfileEnhancementJson = profileEnhancementJson;
        }

        public void UpdatePreferences(string preferencesJson)
        {
            PreferencesJson = preferencesJson;
        }

        public void SetCurrentCv(S3File? currentCv)
        {
            CurrentCv = currentCv;
            CurrentCvId = currentCv?.Id;
        }

        public void AddRelevantDocument(S3File file)
        {
            if (RelevantDocuments.Any(item => item.Id == file.Id))
            {
                return;
            }

            RelevantDocuments.Add(file);
        }
    }
}