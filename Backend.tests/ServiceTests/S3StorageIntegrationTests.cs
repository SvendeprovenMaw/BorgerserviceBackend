using Amazon.S3;
using Amazon.S3.Model;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Enums;
using Backend.api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

public class S3StorageIntegrationTests
{
    private WarehouseDbContext GetDatabaseContext()
    {
        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new WarehouseDbContext(options);
    }

    [Fact]
    public async Task UploadAndDownload_ShouldUseRespectiveMockClients()
    {
        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseInMemoryDatabase(databaseName: "S3DualClientTest")
            .Options;
        var db = new WarehouseDbContext(options);

        var mockUploader = new Mock<IAmazonS3>();
        var mockDownloader = new Mock<IAmazonS3>();
        var mockConf = new Mock<IConfiguration>();

        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(
            mockConf.Object, 
            fileService, 
            consentService, 
            mockUploader.Object, 
            mockDownloader.Object
        );

        var user = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User", "password");
        var fileId = Guid.NewGuid();
        var consentDto = new GiveConsentDto { ConsentGiven = true, TimeOfConsent = DateTime.UtcNow };

        mockUploader.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), default))
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK, ChecksumSHA256 = "hash123" });

        using var ms = new MemoryStream(new byte[10]);
        await s3Service.UploadFile(ms, consentDto, "test.pdf", user, FileCategory.Cv);

        var s3File = new S3File(user, "test.pdf", "key", "hash123", fileId);
        await db.S3Files.AddAsync(s3File);
        await db.SaveChangesAsync();
        
        await s3Service.UserDownloadFile(fileId, user);

        //checks that the correct functions have been called
        mockUploader.Verify(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        
        mockDownloader.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Once);
        
        mockUploader.Verify(x => x.GetPreSignedURL(It.IsAny<GetPreSignedUrlRequest>()), Times.Never);
    }
}