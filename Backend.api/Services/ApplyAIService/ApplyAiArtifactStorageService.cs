using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Configuration;
using Backend.api.Services;
using Microsoft.Extensions.Options;

namespace Backend.api.Services.ApplyAIService;

public interface IApplyAiArtifactStorageService
{
    Task<ApplyAiStoredArtifact> StoreArtifactAsync(
        Guid artifactId,
        string runStoragePrefix,
        string relativePath,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);

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
        : this(backBlazeOptions, CreateS3Client(backBlazeOptions.Value))
    {
    }

    public ApplyAiArtifactStorageService(IOptions<BackBlazeSettings> backBlazeOptions, IAmazonS3 s3)
    {
        var settings = backBlazeOptions.Value;

        _s3 = s3;
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

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return await StoreArtifactAsync(
            artifactId,
            runStoragePrefix,
            StoragePathBuilder.BuildStoredJobPostingRelativePath(artifactId, safeFileName),
            buffer.ToArray(),
            safeFileName,
            string.IsNullOrWhiteSpace(contentType) ? "application/pdf" : contentType,
            cancellationToken);
    }

    public async Task<ApplyAiStoredArtifact> StoreArtifactAsync(
        Guid artifactId,
        string runStoragePrefix,
        string relativePath,
        byte[] content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedRelativePath = NormalizeRelativePath(relativePath, fileName);
        var resolvedFileName = string.IsNullOrWhiteSpace(fileName)
            ? Path.GetFileName(normalizedRelativePath)
            : BuildSafeFileName(fileName, Path.GetFileName(normalizedRelativePath));
        var resolvedContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        var storageKey = StoragePathBuilder.BuildRunArtifactStorageKey(runStoragePrefix, normalizedRelativePath);
        var checksum = Convert.ToHexString(SHA256.HashData(content));

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = new MemoryStream(content, writable: false),
            AutoCloseStream = true,
            ContentType = resolvedContentType,
            ChecksumAlgorithm = ChecksumAlgorithm.SHA256
        };

        await _s3.PutObjectAsync(request, cancellationToken);

        return new ApplyAiStoredArtifact(
            artifactId,
            storageKey,
            normalizedRelativePath,
            resolvedFileName,
            resolvedContentType,
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

    private static string NormalizeRelativePath(string relativePath, string fallbackFileName)
    {
        var candidate = string.IsNullOrWhiteSpace(relativePath)
            ? BuildSafeFileName(fallbackFileName, "artifact.bin")
            : relativePath.Replace('\\', '/').Trim('/');

        var rawSegments = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rawSegments.Length == 0)
        {
            return BuildSafeFileName(fallbackFileName, "artifact.bin");
        }

        var normalizedSegments = rawSegments
            .Select((segment, index) => StoragePathBuilder.BuildSafePathSegment(
                segment,
                index == rawSegments.Length - 1
                    ? BuildSafeFileName(fallbackFileName, "artifact.bin")
                    : "segment"))
            .ToArray();

        return string.Join('/', normalizedSegments);
    }

    private static IAmazonS3 CreateS3Client(BackBlazeSettings settings)
    {
        var config = new AmazonS3Config
        {
            ServiceURL = settings.ServiceUrl,
            ForcePathStyle = settings.ForcePathStyle,
            AuthenticationRegion = settings.UploaderAuthenticationRegion,
            UseHttp = settings.UploaderUseHttp
        };

        var credentials = new BasicAWSCredentials(settings.Keyid, settings.ApplicationKey);
        return new AmazonS3Client(credentials, config);
    }

    private static string TrimSlashes(string value) => value.Trim('/');
}