using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IConsentService
    {
        Task AccecptTerms(User user, Term activeTerms, GiveConsentDto dto);
        Task GiveConsent(User user, S3File s3File, GiveConsentDto dto);
        Task RetractAccountConsent();
        Task<bool> RetractFileConsent(S3File file, User user);
        Task<Consent> VerifyConsent(S3File s3File);
    }

    public class ConsentService : IConsentService
    {
        private readonly WarehouseDbContext _db;
        public ConsentService(WarehouseDbContext db)
        {
            this._db = db;
        }

        public async Task GiveConsent(User user, S3File s3File, GiveConsentDto dto)
        {
            var consent = new Consent(user, s3File, dto.TimeOfConsent);
            await _db.Consents.AddAsync(consent);
            await _db.SaveChangesAsync();
        }

        public async Task<bool> RetractFileConsent(S3File file, User user)
        {
            int rows = await _db.Consents.Where(i => i.FileId == file.Id && i.UserId == user.Id).
            ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsentRetracted, true));

            return rows > 0;
        }

        public async Task AccecptTerms(User user, Term activeTerms, GiveConsentDto dto)
        {
            var consent = new Consent(user, activeTerms, dto.TimeOfConsent);
            await _db.Consents.AddAsync(consent);
            await _db.SaveChangesAsync();
        }

        public async Task RetractAccountConsent()
        {
            // will set all consent linked to the user as retracted
        }

        public async Task<Consent> VerifyConsent(S3File s3File)
        {
            return await _db.Consents.Where(i => i.File.Id == s3File.Id && i.ConsentRetracted == false).FirstAsync();
        }
    }
}