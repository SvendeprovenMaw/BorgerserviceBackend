using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IFileService
    {
        Task<bool> FileUploaded(User user, string filename, string s3Key, FileCategory fileCategory, string bucket, string checksumHash, GiveConsentDto consentDto, Guid fileId);
        Task<S3File?> GetFile(Guid fileId, Guid userId);
        Task<S3File[]> GetUserFiles(Guid userId);
        Task<S3File[]> GetUserFiles(Guid userId, FileCategory fileCategory);
        Task AnonamizeS3Record(Guid fileId, User user);
        Task AnonamizeUserS3Records(User user);
    }

    public class FileService : IFileService
    {
        private ApplyAiDbContext _db;
        private readonly IConsentService _consent;
        public FileService(ApplyAiDbContext db, IConsentService consent)
        {
            this._db = db;
            this._consent = consent;
        }

        public async Task<bool> FileUploaded(User user, string filename, string s3Key, FileCategory fileCategory, string bucket, string checksumHash, GiveConsentDto consentDto, Guid fileId)
        {
            S3File newFile = new(user, filename, s3Key, checksumHash, fileId);
            //cv has to be switched out with new cv when uploaded - if there is an old one it has to be moved to relative folder and be renamed to old cv - keyname has to be switched in s3 and db
            await _db.AddAsync(newFile);
            await _consent.GiveConsent(user, newFile, consentDto);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<S3File?> GetFile(Guid fileId, Guid userId)
        {
            var file = await _db.S3Files.Where(i=>i.Id == fileId).FirstOrDefaultAsync();
            if(file.UserId != userId)
            {
                throw new UnauthorizedAccessException("User does not own this file");
            }
            return file;
        }

        /// <summary>
        /// Gets everyfile even ones not for ai use
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<S3File[]> GetUserFiles(Guid userId)
        {
            var files = await _db.S3Files.Where(i=>i.UserId == userId).ToArrayAsync();
            return files;
        }

        public async Task<S3File[]> GetUserFiles(Guid userId, FileCategory fileCategory)
        {
            string categoryFolder = $"users/{userId}/{fileCategory}/";
            var files = await _db.S3Files.Where(i=>i.UserId == userId && i.S3Key.StartsWith(categoryFolder)).ToArrayAsync();
            return files;
        }

        /// <summary>
        /// gets files that are consented and can be used with the ai
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<S3File[]> GetUserConsentedFiles(Guid userId)
        {
            return await _db.Consents.AsNoTracking()
                    // This filters out any File that is actually a Term
                    .Where(c => c.UserId == userId && !(c.File is Term) && c.ConsentRetracted == false) 
                    .Select(i=>i.File)
                    .ToArrayAsync();
        }

        public async Task AnonamizeUserS3Records(User user)
        {
            await _db.S3Files.Where(i=>i.UserId == user.Id).ExecuteUpdateAsync(i=>i.SetProperty(c=>c.FileName, string.Empty));
        }

        public async Task AnonamizeS3Record(Guid fileId, User user)
        {
            var file = await this.GetFile(fileId, user.Id);
            file.Anonymize();
            await _db.SaveChangesAsync();
        }
    }
}