using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Configuration;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Microsoft.Extensions.Options;
using System.Net;

namespace Backend.api.Services
{
    public interface IS3StorageService
    {
        Task DeleteFileAsync(string bucketname, string filename);
        Task<byte[]> DownloadFileContentAsync(string storageKey, CancellationToken cancellationToken = default);
        Task<S3File[]> GetFileStructure(Guid userId);
        Task<bool> HasObjectTagAsync(string storageKey, string tagKey, string expectedValue, CancellationToken cancellationToken = default);
        Task RegisterExistingFileAsync(string storageKey, GiveConsentDto consentDto, string filename, User user, string checksumHash, Guid? fileIdOverride = null, DateTime? uploadTimeOverride = null);
        Task<string> UserDownloadFile(Guid fileId, User user);
        Task PermentlyUserFilesAsync(string bucketname, Guid userId);
        Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory, Guid? fileIdOverride = null, DateTime? uploadTimeOverride = null, string? storageKeyOverride = null, Dictionary<string, string>? objectTags = null, string? contentTypeOverride = null);
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

        public async Task<byte[]> DownloadFileContentAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            using var response = await s3Uploader.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = storageKey
                },
                cancellationToken);

            await using var output = new MemoryStream();
            await response.ResponseStream.CopyToAsync(output, cancellationToken);
            return output.ToArray();
        }

        public async Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory, Guid? fileIdOverride = null, DateTime? uploadTimeOverride = null, string? storageKeyOverride = null, Dictionary<string, string>? objectTags = null, string? contentTypeOverride = null)
        {
            try
            {
                if (!consentDto.ConsentGiven)
                {
                    throw new ConsentNotGivenException();
                }
                var key = string.IsNullOrWhiteSpace(storageKeyOverride)
                    ? $"users/{user.Id}/{fileCategory}/{Guid.NewGuid()}"
                    : storageKeyOverride;
                var contentType = ResolveContentType(filename, fileCategory, contentTypeOverride);
                var request = CreatePutObjectRequest(key, fileStream, contentType, objectTags);

                PutObjectResponse result;
                try
                {
                    result = await this.s3Uploader.PutObjectAsync(request);
                }
                catch (AmazonS3Exception exception) when (ShouldRetryWithoutObjectTags(exception, objectTags, fileStream))
                {
                    fileStream.Position = 0;
                    request = CreatePutObjectRequest(key, fileStream, contentType, objectTags: null);
                    result = await this.s3Uploader.PutObjectAsync(request);
                }

                await this._files.FileUploaded(user, filename, key, _bucketName, result.ChecksumSHA256, consentDto, fileIdOverride, uploadTimeOverride);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

        public async Task<bool> HasObjectTagAsync(string storageKey, string tagKey, string expectedValue, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await s3Uploader.GetObjectTaggingAsync(
                    new GetObjectTaggingRequest
                    {
                        BucketName = _bucketName,
                        Key = storageKey
                    },
                    cancellationToken);

                return response.Tagging?.Any(tag =>
                    string.Equals(tag.Key, tagKey, StringComparison.Ordinal)
                    && string.Equals(tag.Value, expectedValue, StringComparison.Ordinal)) == true;
            }
            catch (AmazonS3Exception exception) when (
                exception.StatusCode == HttpStatusCode.NotFound
                || string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal))
            {
                return false;
            }
        }

        public async Task RegisterExistingFileAsync(string storageKey, GiveConsentDto consentDto, string filename, User user, string checksumHash, Guid? fileIdOverride = null, DateTime? uploadTimeOverride = null)
        {
            await _files.FileUploaded(user, filename, storageKey, _bucketName, checksumHash, consentDto, fileIdOverride, uploadTimeOverride);
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

        private static string ResolveContentType(string filename, FileCategory fileCategory, string? contentTypeOverride)
        {
            if (!string.IsNullOrWhiteSpace(contentTypeOverride))
            {
                return contentTypeOverride;
            }

            return string.Equals(Path.GetExtension(filename), ".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : fileCategory == FileCategory.Cv
                    ? "application/pdf"
                    : "image/jpeg";
        }

        private PutObjectRequest CreatePutObjectRequest(string key, Stream fileStream, string contentType, Dictionary<string, string>? objectTags)
        {
            var request = new PutObjectRequest
            {
            BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = contentType,
                ChecksumAlgorithm = ChecksumAlgorithm.SHA256
            };

            if (objectTags is { Count: > 0 })
            {
                request.TagSet = objectTags
                    .Select(tag => new Tag { Key = tag.Key, Value = tag.Value })
                    .ToList();
            }

            return request;
        }

        private static bool ShouldRetryWithoutObjectTags(AmazonS3Exception exception, Dictionary<string, string>? objectTags, Stream fileStream)
        {
            return objectTags is { Count: > 0 }
                && fileStream.CanSeek
                && exception.Message.Contains("Unsupported header 'x-amz-tagging'", StringComparison.OrdinalIgnoreCase);
        }
    }
}