using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Configuration;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Microsoft.Extensions.Options;

namespace Backend.api.Services
{
    public interface IS3StorageService
    {
        Task DeleteFileAsync(string bucketname, string filename);
        Task<S3File[]> GetFileStructure(Guid userId);
        Task<string> UserDownloadFile(Guid fileId, User user);
        Task PermentlyUserFilesAsync(string bucketname, Guid userId);
        Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory);
    }

    public class S3StorageService : IS3StorageService
    {
        private readonly IFileService _files;
        private readonly IConsentService _consent;
        private readonly IAmazonS3 s3Uploader;
        private readonly IAmazonS3 s3Downloader;
        private readonly string _bucketName;

        public S3StorageService(IOptions<BackBlazeSettings> backBlazeOptions, IFileService files, IConsentService consent)
        {
            this._consent = consent;
            this._files = files;
            var settings = backBlazeOptions.Value;

            var downloaderConfig = new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,
                AuthenticationRegion = settings.DownloaderAuthenticationRegion,
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(settings.DownloaderRegionSystemName),
                ForcePathStyle = settings.ForcePathStyle,
            };

            var uploaderConfig = new AmazonS3Config
            {
                ServiceURL = settings.ServiceUrl,
                ForcePathStyle = settings.ForcePathStyle,
                AuthenticationRegion = settings.UploaderAuthenticationRegion,
                UseHttp = settings.UploaderUseHttp
            };

            _bucketName = settings.Bucket;

            var credentials = new Amazon.Runtime.BasicAWSCredentials(settings.Keyid, settings.ApplicationKey);
            s3Downloader = new AmazonS3Client(
                credentials,
                downloaderConfig
            );
            s3Uploader = new AmazonS3Client(
                credentials,
                uploaderConfig
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
                    BucketName = _bucketName,
                    Key = key,
                    InputStream = fileStream,
                    ContentType = fileCategory == FileCategory.Cv ? "application/pdf" : "image/jpeg",
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256
                };
                var result = await this.s3Uploader.PutObjectAsync(request);
                await this._files.FileUploaded(user, filename, key, _bucketName, result.ChecksumSHA256, consentDto);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public async Task<string> UserDownloadFile(Guid fileId, User user)
        {
            var s3File = await _files.GetFile(fileId, user.Id);

            if (s3File is null)
            {
                throw new FileNotFoundException($"No file with id '{fileId}' was found for user '{user.Id}'.");
            }

            Console.WriteLine($"{s3File.FileName}, {fileId}, { user.Id}");
            string urlString = s3Downloader.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = s3File.S3Key,
                Expires = DateTime.UtcNow.AddMinutes(5),
                Verb = HttpVerb.GET
            });
            return urlString;
        }

        public async Task<string> AiDownloadUserFile(Guid fileId, User user)
        {
            var s3File = await _files.GetFile(fileId, user.Id);

            if (s3File is null)
            {
                throw new FileNotFoundException($"No file with id '{fileId}' was found for user '{user.Id}'.");
            }

            var consent = await _consent.VerifyConsent(s3File);

            if (consent.ConsentRetracted)
            {
                throw new FileNotFoundException("Consent retracted or file deleted.");
            }

            string urlString = s3Downloader.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = s3File.S3Key,
                Expires = DateTime.UtcNow.AddMinutes(5),
                Verb = HttpVerb.GET
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