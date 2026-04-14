using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Backend.api.Enums;

namespace Backend.api.Services
{
    public class S3StorageService
    {
        private IConfiguration _conf;
        AmazonS3Client s3Client;
        AmazonS3Config config = new AmazonS3Config
        {
            ServiceURL = "https://s3.eu-central-003.backblazeb2.com"
        };
        public S3StorageService(IConfiguration conf)
        {
            this._conf = conf;
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

        public async Task UploadFile(Stream fileStream, string filename, string bucketName, Guid userid, FileCategory fileCategory)
        {
            try
            {
                var key = $"users/{userid}/{fileCategory}/{filename}";
                var request = new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = key,
                    InputStream = fileStream,
                    ContentType = fileCategory == FileCategory.Cv ? "application/pdf" : "image/jpeg"
                };
                await this.s3Client.PutObjectAsync(request);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public async Task UploadFile(Stream file, string bucketName)
        {
            try
            {
                var fileTransferUtility = new TransferUtility(s3Client);
                await fileTransferUtility.UploadAsync(file, bucketName, "filename.pdf");
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
                Key        = filename,
                Expires    = DateTime.UtcNow.AddMinutes(5) // Link works for 1 hour
            });
            return urlString;
        }

        public async Task UploadFileAsync()
        {
            
        }
        public async Task DeleteFileAsync()
        {
            
        }
        public async Task CreateFileAsync()
        {
            
        }
        public async Task PermentlyUserFilesAsync()
        {
            
        }
    }
}