using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IFileService
    {
        Task<bool> FileUploaded(User user, string filename, string s3Key, string bucket, string checksumHash, GiveConsentDto consentDto);
        Task<S3File?> GetFile(Guid fileId, Guid userId);
        Task<S3File[]> GetUserFiles(Guid userId);
    }

    public class FileService : IFileService
    {
        private WarehouseDbContext _db;
        private readonly IConsentService _consent;
        public FileService(WarehouseDbContext db, IConsentService consent)
        {
            this._db = db;
            this._consent = consent;
        }

        public async Task<bool> FileUploaded(User user, string filename, string s3Key, string bucket, string checksumHash, GiveConsentDto consentDto)
        {
            S3File newFile = new(user, filename, s3Key, checksumHash);
            await _db.AddAsync(newFile);
            await _consent.GiveConsent(user, newFile, consentDto);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<S3File?> GetFile(Guid fileId, Guid userId)
        {
            var file = await _db.S3Files.Where(i=>i.UserId == userId && i.Id == fileId).FirstOrDefaultAsync();
            return file;
        }

        public async Task<S3File[]> GetUserFiles(Guid userId)
        {
            var files = await _db.S3Files.Where(i=>i.UserId == userId).ToArrayAsync();
            return files;
        }
    }
}