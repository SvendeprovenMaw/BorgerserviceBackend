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
        Task<S3File[]> GetFileStructure(Guid userId);
        Task<string> LinkToFIle(Guid fileId, User user);
        Task PermentlyUserFilesAsync(string bucketname, Guid userId);
        Task UploadFile(Stream fileStream, string filename, User user, FileCategory fileCategory);
    }

    public class S3StorageService : IS3StorageService
    {
        private IConfiguration _conf;
        private IFileService _files;
        AmazonS3Client s3Client;
        public S3StorageService(IConfiguration conf, IFileService files)
        {
            var config = new AmazonS3Config 
            { 
                ServiceURL = "https://s3.eu-central-003.backblazeb2.com",
                // This is the missing piece!
                // 2. Use a "real" RegionEndpoint object instead of just a string.
                // Even though it's BackBlaze, the SDK needs this to avoid the NullRef.
                RegionEndpoint = Amazon.RegionEndpoint.EUCentral1, 
                
                // 3. This tells the SDK to use the URL provided above for the actual call
                ForcePathStyle = true
            };
            this._conf = conf;
            this._files = files;
            this.s3Client = new(_conf["BackBlaze:Keyid"], _conf["BackBlaze:ApplicationKey"], config);
        }

        public async Task<S3File[]> GetFileStructure(Guid userId)
        {
            var response = await _files.GetUserFiles(userId);
            return response;
        }

        public async Task UploadFile(Stream fileStream, string filename, User user, FileCategory fileCategory)
        {
            try
            {
                var key = $"users/{user.Id}/{fileCategory}/{Guid.NewGuid()}";
                var request = new PutObjectRequest
                {
                    BucketName = _conf["BackBlaze:KeyName"],
                    Key = key,
                    InputStream = fileStream,
                    ContentType = fileCategory == FileCategory.Cv ? "application/pdf" : "image/jpeg",
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var result = await this.s3Client.PutObjectAsync(request);
                await this._files.FileUploaded(user, filename, key, _conf["BackBlaze:KeyName"], result.ChecksumSHA256);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public async Task<string> LinkToFIle(Guid fileId, User user)
        {
            var s3File = await _files.GetFile(fileId, user.Id);
            Console.WriteLine(s3File.S3Key);
            string urlString = s3Client.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _conf["BackBlaze:KeyName"],
                Key = s3File.S3Key,
                Expires = DateTime.UtcNow.AddMinutes(5)
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