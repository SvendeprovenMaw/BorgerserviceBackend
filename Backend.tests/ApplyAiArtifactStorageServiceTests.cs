using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Configuration;
using Backend.api.Services.ApplyAIService;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;

namespace Backend.tests;

public sealed class ApplyAiArtifactStorageServiceTests
{
    [Fact]
    public async Task StoreJobPostingAsync_SanitizesInputFileNameAndWritesUnderInputsJobListing()
    {
        var capturedRequests = new List<PutObjectRequest>();
        var service = CreateService(capturedRequests: capturedRequests);

        await service.StoreJobPostingAsync(Guid.NewGuid(), "users/test/Runs/2026-04-19/job-1", new MemoryStream(Encoding.UTF8.GetBytes("pdf")), "Job posting?.pdf", "application/pdf");

        capturedRequests.Should().ContainSingle();
        capturedRequests[0].Key.Should().Contain("inputs/job_listing/");
        capturedRequests[0].Key.Should().Contain("Job-posting-.pdf");
    }

    [Fact]
    public async Task StoreArtifactAsync_NormalizesRelativePathSegments()
    {
        var capturedRequests = new List<PutObjectRequest>();
        var service = CreateService(capturedRequests: capturedRequests);

        var stored = await service.StoreArtifactAsync(Guid.NewGuid(), "users/test/Runs/2026-04-19/job-1", "../verification//phase.json", Encoding.UTF8.GetBytes("{}"), "phase.json", "application/json");

        stored.RelativePath.Should().Be("segment/verification/phase.json");
        capturedRequests[0].Key.Should().EndWith("segment/verification/phase.json");
    }

    [Fact]
    public async Task StoreArtifactAsync_BuildsStorageKeyUnderCanonicalRunPrefix()
    {
        var capturedRequests = new List<PutObjectRequest>();
        var service = CreateService(capturedRequests: capturedRequests);

        var stored = await service.StoreArtifactAsync(Guid.NewGuid(), "users/111/Runs/2026-04-19/job-1", "requirements.json", Encoding.UTF8.GetBytes("{}"), "requirements.json", "application/json");

        stored.StorageKey.Should().Be("users/111/Runs/2026-04-19/job-1/requirements.json");
        capturedRequests[0].Key.Should().Be("users/111/Runs/2026-04-19/job-1/requirements.json");
    }

    [Fact]
    public async Task StoreArtifactAsync_FallsBackToApplicationOctetStreamWhenNoContentTypeIsProvided()
    {
        var service = CreateService();

        var stored = await service.StoreArtifactAsync(Guid.NewGuid(), "users/test/Runs/2026-04-19/job-1", "artifact.bin", Encoding.UTF8.GetBytes("data"), "artifact.bin", string.Empty);

        stored.MediaType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task StoreArtifactAsync_ReturnsSha256ChecksumForStoredBytes()
    {
        var service = CreateService();

        var stored = await service.StoreArtifactAsync(Guid.NewGuid(), "users/test/Runs/2026-04-19/job-1", "artifact.txt", Encoding.UTF8.GetBytes("checksum-test"), "artifact.txt", "text/plain");

        stored.Checksum.Should().NotBeNullOrWhiteSpace();
        stored.Checksum.Should().HaveLength(64);
    }

    [Fact]
    public async Task DownloadArtifactAsync_PreservesMediaTypeFallbackAndFileNameFallbackBehavior()
    {
        var getObjectResponse = new GetObjectResponse
        {
            ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes("artifact-content")),
        };
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(client => client.GetObjectAsync("applyai-tests", "users/test/Runs/job-1/cover_letter/cover_letter.pdf", It.IsAny<CancellationToken>()))
            .ReturnsAsync(getObjectResponse);

        var service = new ApplyAiArtifactStorageService(CreateOptions(), s3.Object);

        var response = await service.DownloadArtifactAsync("users/test/Runs/job-1/cover_letter/cover_letter.pdf", string.Empty, string.Empty);

        response.MediaType.Should().Be("application/octet-stream");
        response.FileName.Should().Be("cover_letter.pdf");
        Encoding.UTF8.GetString(response.Content).Should().Be("artifact-content");
    }

    private static ApplyAiArtifactStorageService CreateService(List<PutObjectRequest>? capturedRequests = null)
    {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(client => client.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, _) => capturedRequests?.Add(request))
            .ReturnsAsync(new PutObjectResponse());

        return new ApplyAiArtifactStorageService(CreateOptions(), s3.Object);
    }

    private static IOptions<BackBlazeSettings> CreateOptions()
    {
        return Options.Create(new BackBlazeSettings
        {
            Bucket = "applyai-tests",
            ServiceUrl = "https://s3.example.com",
            ForcePathStyle = true,
            UploaderAuthenticationRegion = "eu-west-1",
            UploaderUseHttp = false,
            Keyid = "key-id",
            ApplicationKey = "secret",
        });
    }
}