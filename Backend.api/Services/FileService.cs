using Backend.api.Database;
using Backend.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services
{
    public interface IFileService
    {
        Task<bool> FileUploaded(User user, string filename, string s3Key, string bucket, string checksumHash);
        Task<S3File?> GetFile(Guid fileId, Guid userId);
    }

    public class FileService : IFileService
    {
        private WarehouseDbContext _db;
        public FileService(WarehouseDbContext db)
        {
            this._db = db;
        }

        public async Task<bool> FileUploaded(User user, string filename, string s3Key, string bucket, string checksumHash)
        {
            S3File newFile = new(user, s3Key, checksumHash);
            await _db.AddAsync(newFile);
            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<S3File?> GetFile(Guid fileId, Guid userId)
        {
            var file = await _db.S3Files.Where(i=>i.UserId == userId && i.Id == fileId).FirstOrDefaultAsync();
            return file;
        }
    }
}