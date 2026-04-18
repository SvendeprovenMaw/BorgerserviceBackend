using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Configuration;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService;

public interface IApplyAiArtifactStorageService
{
    Task<ApplyAiStoredArtifact> StoreJobPostingAsync(
        Guid jobId,
        string runStoragePrefix,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<ApplyAiArtifactContentResponse> DownloadArtifactAsync(
        string storageKey,
        string fileName,
        string mediaType,
        CancellationToken cancellationToken = default);
}

public sealed class ApplyAiArtifactStorageService : IApplyAiArtifactStorageService
{
    private static readonly Regex InvalidFileNameCharacters = new("[^A-Za-z0-9._-]", RegexOptions.Compiled);

    private readonly IAmazonS3 _s3;
    private readonly string _bucketName;

    public ApplyAiArtifactStorageService(IOptions<BackBlazeSettings> backBlazeOptions)
    {
        var settings = backBlazeOptions.Value;

        var config = new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = settings.ForcePathStyle,
            AuthenticationRegion = settings.UploaderAuthenticationRegion,
            UseHttp = settings.UploaderUseHttp
        };

        var credentials = new BasicAWSCredentials(settings.Keyid, settings.ApplicationKey);
        _s3 = new AmazonS3Client(credentials, config);
        _bucketName = settings.Bucket;
    }

    public async Task<ApplyAiStoredArtifact> StoreJobPostingAsync(
        Guid jobId,
        string runStoragePrefix,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var artifactId = Guid.NewGuid();
        var safeFileName = BuildSafeFileName(fileName, "job-posting.pdf");
        var relativePath = $"inputs/job-listing/{artifactId:N}__{safeFileName}";
        var storageKey = $"{TrimSlashes(runStoragePrefix)}/{relativePath}";

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        buffer.Position = 0;

        var checksum = Convert.ToHexString(SHA256.HashData(bytes));

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = new MemoryStream(bytes),
            AutoCloseStream = true,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };

        await _s3.PutObjectAsync(request, cancellationToken);

        return new ApplyAiStoredArtifact(
            artifactId,
            storageKey,
            relativePath,
            safeFileName,
            request.ContentType,
            checksum);
    }

    public async Task<ApplyAiArtifactContentResponse> DownloadArtifactAsync(
        string storageKey,
        string fileName,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        var response = await _s3.GetObjectAsync(_bucketName, storageKey, cancellationToken);
        await using var responseStream = response.ResponseStream;
        await using var buffer = new MemoryStream();
        await responseStream.CopyToAsync(buffer, cancellationToken);

        return new ApplyAiArtifactContentResponse(
            buffer.ToArray(),
            string.IsNullOrWhiteSpace(mediaType) ? response.Headers.ContentType ?? "application/octet-stream" : mediaType,
            string.IsNullOrWhiteSpace(fileName) ? Path.GetFileName(storageKey) : fileName);
    }

    private static string BuildSafeFileName(string? fileName, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(fileName) ? fallback : fileName.Trim();
        var safe = InvalidFileNameCharacters.Replace(trimmed, "-");
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string TrimSlashes(string value) => value.Trim('/');
}