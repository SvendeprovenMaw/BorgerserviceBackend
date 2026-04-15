using Backend.api.Database;
using Backend.api.Entities;

namespace Backend.api.Services
{
    public interface IFileService
    {
        Task<bool> FileUploaded(User user, string s3Key, string bucket, string checksumHash);
    }

    public class FileService : IFileService
    {
        private WarehouseDbContext _db;
        public FileService(WarehouseDbContext db)
        {
            this._db = db;
        }

        public async Task<bool> FileUploaded(User user, string s3Key, string bucket, string checksumHash)
        {
            S3File newFile = new(user, s3Key, checksumHash);
            await _db.AddAsync(newFile);
            await _db.SaveChangesAsync();
            return true;
        }
    }
}