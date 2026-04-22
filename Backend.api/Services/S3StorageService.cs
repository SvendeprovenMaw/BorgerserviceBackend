using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace Backend.api.Services
{
    public interface IS3StorageService
    {
        Task DeleteFileAsync(Guid fileId, User user);
        Task DeleteFilesAsync(User user);
        Task<S3File[]> GetFileStructure(Guid userId);
        Task<string> UserDownloadFile(Guid fileId, User user);
        Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory);
    }

    public class S3StorageService : IS3StorageService
    {
        private IConfiguration _conf;
        private IFileService _files;
        private readonly IConsentService _consent;
        IAmazonS3 s3Uploader;
        IAmazonS3 s3Downloader;
        public S3StorageService(IConfiguration conf, IFileService files, IConsentService consent)
        {
            this._consent = consent;
            this._conf = conf;
            this._files = files;

            var downloaderConfig = new AmazonS3Config 
            { 
                ServiceURL = "https://s3.eu-central-003.backblazeb2.com",
                AuthenticationRegion = "eu-central-1", 
                RegionEndpoint = Amazon.RegionEndpoint.EUCentral1,
                ForcePathStyle = true,
                
            };

             var uploaderconfig = new AmazonS3Config 
            { 
                ServiceURL = "https://s3.eu-central-003.backblazeb2.com",
                ForcePathStyle = true,
                AuthenticationRegion = "eu-central-003", // important!
                UseHttp = false
                
            };
            

            var keyId = conf["BackBlaze:Keyid"];
            var appKey = conf["BackBlaze:ApplicationKey"];

            var credentials = new Amazon.Runtime.BasicAWSCredentials(keyId, appKey);
            this.s3Downloader = new AmazonS3Client(
                credentials,
                downloaderConfig
            );
            this.s3Uploader = new AmazonS3Client(
                credentials,
                uploaderconfig
            );
        }

        public async Task<S3File[]> GetFileStructure(Guid userId)
        {
            var response = await _files.GetUserFiles(userId);
            return response;
        }

        public async Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory)
        {
            try
            {
                if (!consentDto.ConsentGiven)
                {
                    throw new ConsentNotGivenException();
                }
                var key = $"users/{user.Id}/{fileCategory}/{Guid.NewGuid()}";
                var request = new PutObjectRequest
                {
                    BucketName = _conf["BackBlaze:KeyName"],
                    Key = key,
                    InputStream = fileStream,
                    ContentType = fileCategory == FileCategory.Cv ? "application/pdf" : "image/jpeg",
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var result = await this.s3Uploader.PutObjectAsync(request);
                await this._files.FileUploaded(user, filename, key, _conf["BackBlaze:KeyName"], result.ChecksumSHA256, consentDto);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public async Task<string> UserDownloadFile(Guid fileId, User user)
        {
            var s3File = await _files.GetFile(fileId, user.Id);
            if(s3File == null)
            {
                throw new FileNotFoundException($"file deleted  fileId:{fileId}, UserId:{user.Id}");
            }
            string urlString = s3Downloader.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _conf["BackBlaze:KeyName"],
                Key = s3File.S3Key,
                Expires = DateTime.UtcNow.AddMinutes(5),
                Verb = HttpVerb.GET
            });
            return urlString;
        }

        public async Task<S3File[]> GetRelevantUserFiles(Guid userId)
        {
            var response = await _files.GetUserFiles(userId, FileCategory.ReleventDocuments);
            return response;
        }

        public async Task DeleteFileAsync(Guid fileId, User user)
        {
            S3File s3file = await _files.GetFile(fileId, user.Id);
            DeleteObjectRequest request = new()
            {
                BucketName = _conf["BackBlaze:KeyName"],
                Key = s3file.S3Key
            };
            await this.s3Uploader.DeleteObjectAsync(request);
        }

        public async Task DeleteFilesAsync(User user)
        {
            string prefix = $"users/{user.Id}/";

            try
            {
                var listRequest = new ListObjectsV2Request
                {
                    BucketName = _conf["BackBlaze:KeyName"],
                    Prefix = prefix
                };

                ListObjectsV2Response listResponse;
                int maxRuns = 3;
                int runs = 0;
                do
                {
                    listResponse = await s3Uploader.ListObjectsV2Async(listRequest);

                    if (listResponse.S3Objects.Count > 0)
                    {
                        var deleteRequest = new DeleteObjectsRequest
                        {
                            BucketName = _conf["BackBlaze:KeyName"],
                            Objects = listResponse.S3Objects
                                .Select(o => new KeyVersion { Key = o.Key })
                                .ToList()
                        };

                        await s3Uploader.DeleteObjectsAsync(deleteRequest);
                    }

                    listRequest.ContinuationToken = listResponse.NextContinuationToken;
                    runs++;
                } while (listResponse.IsTruncated ?? false && runs >= maxRuns);//checks if there are more files in the s3 storage and runs again if there are can only run 3 times max
            }
            catch (AmazonS3Exception e)
            {
                throw new Exception($"Kunne ikke slette brugerens filer: {e.Message}");
            }
        }
    }
}