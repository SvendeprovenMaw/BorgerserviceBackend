using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;

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
        IAmazonS3 s3Downloader; // seperate s3 clients is needed since we use backblaze and aws S3 library tries to validate against aws endpoints which causes issues when downloading files from backblaze, using a seperate client without validation for downloading files solves this issue
        public S3StorageService(IConfiguration conf, IFileService files, IConsentService consent, [FromKeyedServices("S3Uploader")] IAmazonS3 s3Uploader, [FromKeyedServices("S3Downloader")] IAmazonS3 s3Downloader)
        {
            this._consent = consent;
            this._conf = conf;
            this._files = files;
            this.s3Uploader = s3Uploader;
            this.s3Downloader = s3Downloader;
        }

        /// <summary>
        /// gets all s3 files from so frontend can show it
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<S3File[]> GetFileStructure(Guid userId)
        {
            var response = await _files.GetUserFiles(userId);
            return response;
        }

        /// <summary>
        /// uploads a file to s3 storage, records consent, save record in database and links it to user
        /// </summary>
        /// <param name="fileStream"></param>
        /// <param name="consentDto"></param>
        /// <param name="filename"></param>
        /// <param name="user"></param>
        /// <param name="fileCategory"></param>
        /// <returns></returns>
        public async Task UploadFile(Stream fileStream, GiveConsentDto consentDto, string filename, User user, FileCategory fileCategory)
        {
            try
            {
                if (!consentDto.ConsentGiven)
                {
                    throw new ConsentNotGivenException();
                }
                Guid id = Guid.NewGuid();
                var key = $"users/{user.Id}/{fileCategory}/{id}";
                var request = new PutObjectRequest
                {
                    BucketName = _conf["BackBlaze:KeyName"],
                    Key = key,
                    InputStream = fileStream,
                    ContentType = "application/pdf",
                    ChecksumAlgorithm = ChecksumAlgorithm.SHA256//ensures file tampering can be detected
                };
                var result = await this.s3Uploader.PutObjectAsync(request);

                await this._files.FileUploaded(user, filename, key, fileCategory, _conf["BackBlaze:KeyName"]!, result.ChecksumSHA256, consentDto, id);
            }
            catch (System.Exception)
            {
                throw;
            }
        }

/// <summary>
/// Creates a pre-signed url for the user to download the file directly from s3 storage, checks if user has consent and if file belongs to user before allowing download
/// </summary>
/// <param name="fileId"></param>
/// <param name="user"></param>
/// <returns></returns>
/// <exception cref="FileNotFoundException"></exception>
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

/// <summary>
/// Get files as binary data so it can be sent to ai for processing
/// </summary>
/// <param name="s3Files"></param>
/// <returns></returns>
/// <exception cref="Exception"></exception>
        public async Task<ICollection<BinaryData>> GetFilesAsBinaryDataAsync(S3File[] s3Files)
        {
            var filesData = new List<BinaryData>();
            string[] keys = s3Files.Select(f => f.S3Key).ToArray();
            foreach (var file in s3Files)
            {
                try
                {
                    var request = new GetObjectRequest
                    {
                        BucketName = _conf["BackBlaze:KeyName"],
                        Key = file.S3Key
                    };

                    using GetObjectResponse response = await s3Downloader.GetObjectAsync(request);

                    BinaryData fileData = await BinaryData.FromStreamAsync(response.ResponseStream);
                    
                    filesData.Add(fileData);
                }
                catch (Amazon.S3.AmazonS3Exception ex)
                {
                    throw new Exception($"Kunne ikke hente filen {file.S3Key} fra S3: {ex.Message}", ex);
                }
            }
            return filesData;
        }

/// <summary>
/// Gets all files in the relevant documents category for a user, used for ai processing of user profile in phase 3
/// </summary>
/// <param name="userId"></param>
/// <returns></returns>
        public async Task<S3File[]> GetRelevantUserFiles(Guid userId)
        {
            var response = await _files.GetUserFiles(userId, FileCategory.ReleventDocuments);
            return response;
        }

/// <summary>
/// Deletes a user file frin s3 and retracts consent for the file
/// </summary>
/// <param name="fileId"></param>
/// <param name="user"></param>
/// <returns></returns>
        public async Task DeleteFileAsync(Guid fileId, User user)
        {
            S3File s3file = await _files.GetFile(fileId, user.Id);
            await this._consent.RetractFileConsent(s3file, user);
            DeleteObjectRequest request = new()
            {
                BucketName = _conf["BackBlaze:KeyName"],
                Key = s3file.S3Key
            };
            await _files.AnonamizeS3Record(fileId, user);
            await this.s3Uploader.DeleteObjectAsync(request);
        }

/// <summary>
/// Deletes all user's files in s3 storage
/// </summary>
/// <param name="user"></param>
/// <returns></returns>
/// <exception cref="Exception"></exception>
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
                    if(listResponse == null || listResponse.S3Objects == null)
                    {
                        return;
                    }
                    if (listResponse?.S3Objects?.Count > 0)
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