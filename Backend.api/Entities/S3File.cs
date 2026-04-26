using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Backend.api.Entities
{
    public class S3File
    {
        protected S3File(){}
        public S3File(User user, string filename, string s3Key, string checksumHash)
        {
            this.Id = Guid.NewGuid();
            this.UploadTime = DateTime.UtcNow;
            this.UserId = user.Id;
            this.S3Key = s3Key;
            this.ChecksumHash = checksumHash;
            this.FileName = filename;
        }
        public S3File(User user, string filename, string s3Key, string checksumHash, Guid fileId)
        {
            this.Id = fileId;
            this.UploadTime = DateTime.UtcNow;
            this.UserId = user.Id;
            this.S3Key = s3Key;
            this.ChecksumHash = checksumHash;
            this.FileName = filename;
            
        }
        public Guid Id { get; private set; }
        public Guid UserId { get; private set; }
        public User User { get; private set; }
        public string FileName { get; set; } = string.Empty;
        public string S3Key { get; private set; } = "";
        public string ChecksumHash { get; private set; } = "";
        public DateTime UploadTime { get; set; }


        public void Anonymize()
        {
            this.FileName = "Anonymiseret";
            this.S3Key = "Anonymiseret";
            this.ChecksumHash = "Anonymiseret";
        }
    }
}