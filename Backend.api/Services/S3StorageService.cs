using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Enums;

namespace Backend.api.Services
{
    public interface IS3StorageService
    {
        Task DeleteFileAsync(string bucketname, string filename);
        Task<ListObjectsV2Response> GetFileStructure(string bucketName);
        Task<string> LinkToFIle(string bucketname, string filename);
        Task PermentlyUserFilesAsync(string bucketname, Guid userId);
        Task UploadFile(Stream fileStream, string filename, string bucketName, User user, FileCategory fileCategory);
    }

    public class S3StorageService : IS3StorageService
    {
        private IConfiguration _conf;
        private IFileService _files;
        AmazonS3Client s3Client;
        AmazonS3Config config = new AmazonS3Config
        {
            ServiceURL = "https://s3.eu-central-003.backblazeb2.com"
        };
        public S3StorageService(IConfiguration conf, IFileService files)
        {
            this._conf = conf;
            this._files = files;
            this.s3Client = new(_conf["backblaze:keyid"], _conf["backblaze:applicationkey"], config);
        }

        public async Task<ListObjectsV2Response> GetFileStructure(string bucketName)
        {
            var response = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = "users/"
            });
            return response;
        }

        public async Task UploadFile(Stream fileStream, string filename, string bucketName, User user, FileCategory fileCategory)
        {
            try
            {
                var key = $"users/{user.Id}/{fileCategory}/{filename}";
                var request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = fileStream,
                    ContentType = fileCategory == FileCategory.Cv ? "application/pdf" : "image/jpeg",
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var result = await this.s3Client.PutObjectAsync(request);
                await this._files.FileUploaded(user, key, bucketName, result.ChecksumSHA256);
            }
            catch (System.Exception)
            {
                throw;
            }
        }


        public async Task<string> LinkToFIle(string bucketname, string filename)
        {
            string urlString = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = bucketname,
                Key = filename,
                Expires = DateTime.UtcNow.AddMinutes(5) // Link works for 1 hour
            });
            return urlString;
        }

        public async Task DeleteFileAsync(string bucketname, string filename)
        {

        }

        public async Task PermentlyUserFilesAsync(string bucketname, Guid userId)
        {

        }
    }
}