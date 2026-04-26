using Amazon.S3;
using Amazon.S3.Model;
using Backend.api;
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
    private readonly ITestOutputHelper _output;
    public S3StorageIntegrationTests(ITestOutputHelper output)
    {
        this._output = output;
    }
    private WarehouseDbContext GetDatabaseContext()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<WarehouseDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new WarehouseDbContext(options);
        context.Database.EnsureCreated(); // Creates the tables
        return context;
    }

    [Fact]
    public async Task UploadAndDownload_mock()
    {
        var db = GetDatabaseContext();

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

    [Fact]
    public async Task S3Service_UnhappyPath_ShouldThrowUnAthorizedAccessException_WhenUnauthorizedUserRequestsFile() // ikke funktionelle krav 3
    {
        var db = GetDatabaseContext(); //creates in memory database

#region S3ServiceMock
        var mockConf = new Mock<IConfiguration>();
        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var virtualBucket = new Dictionary<string, byte[]>();

        var mockUploader = new Mock<IAmazonS3>();
        var mockDownloader = new Mock<IAmazonS3>();

        mockUploader.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PutObjectRequest, CancellationToken>((request, token) => {
                using var ms = new MemoryStream();
                request.InputStream.CopyTo(ms);
                virtualBucket[request.Key] = ms.ToArray(); // Save to memory
            })
            .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK, ChecksumSHA256 = "mock-checksum-hash-value" });

        mockDownloader.Setup(x => x.GetObjectAsync(It.IsAny<GetObjectRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetObjectRequest request, CancellationToken token) => {
                var response = new GetObjectResponse();
                if (virtualBucket.TryGetValue(request.Key, out var data)) {
                    response.ResponseStream = new MemoryStream(data);
                    response.HttpStatusCode = System.Net.HttpStatusCode.OK;
                }
                return response;
            });

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(
            mockConf.Object, 
            fileService, 
            consentService, 
            mockUploader.Object, 
            mockDownloader.Object
        );
#endregion

        var UploadUser = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User", "password");
        var UnauthorizedUser = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Unauthorized User", "password");
        var fileId = Guid.NewGuid();
        var consentDto = new GiveConsentDto { ConsentGiven = true, TimeOfConsent = DateTime.UtcNow };

        using var ms = new MemoryStream(new byte[10]);
        await s3Service.UploadFile(ms, consentDto, "test.pdf", UploadUser, FileCategory.Cv);

        var s3File = new S3File(UploadUser, "test.pdf", "key", "hash123", fileId);
        await db.S3Files.AddAsync(s3File);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
        {
            string url = await s3Service.UserDownloadFile(s3File.Id, UnauthorizedUser);
        });

    }

    [Fact]
    public async Task S3Storage_UploadTests_ShouldVerifyCorrectPathStructure() //funktionelle krav 07
    {
        var db = GetDatabaseContext();
        var virtualBucket = new Dictionary<string, byte[]>();
        var mockUploader = new Mock<IAmazonS3>();
        
        mockUploader.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
        .Callback<PutObjectRequest, CancellationToken>((request, token) => {
            using var ms = new MemoryStream();
            request.InputStream.CopyTo(ms);
            virtualBucket[request.Key] = ms.ToArray();
        })
        .ReturnsAsync(new PutObjectResponse { 
            HttpStatusCode = System.Net.HttpStatusCode.OK, 
            ChecksumSHA256 = "mock-hash" 
        });

        var mockConf = new Mock<IConfiguration>();
        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(mockConf.Object, fileService, consentService, mockUploader.Object, new Mock<IAmazonS3>().Object);

        var testUser1 = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User1", "password");
        var testUser2 = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User2", "password");
        var testUser3 = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User3", "password");
        var testUser4 = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User4", "password");
        var testUser5 = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User5", "password");

        await db.Users.AddRangeAsync(new []{testUser1, testUser2, testUser3, testUser4, testUser5});
        
        var testScenarios = new List<(User User, FileCategory Category, string FileName)>
        {
            (testUser1, FileCategory.Cv, "cv1.pdf"),
            (testUser2, FileCategory.ReleventDocuments, "doc1.pdf"),
            (testUser3, FileCategory.Cv, "cv2.pdf"),
            (testUser4, FileCategory.ReleventDocuments, "doc2.pdf"),
            (testUser5, FileCategory.Cv, "cv3.pdf")
        };

        int successfulUploads = 0;
        var consentDto = new GiveConsentDto { ConsentGiven = true, TimeOfConsent = DateTime.UtcNow };

        foreach (var scenario in testScenarios)
        {
            using var ms = new MemoryStream(new byte[10]);
            await s3Service.UploadFile(ms, consentDto, scenario.FileName, scenario.User, scenario.Category);

            var uploadKey = virtualBucket.Keys.FirstOrDefault(k => k.Contains(scenario.User.Id.ToString()));

            if (uploadKey != null && 
                uploadKey.StartsWith($"users/{scenario.User.Id}/") && //check if s3key is in the users prefix
                uploadKey.Contains($"/{scenario.Category}/")) // checks if the s3key has the the correct category prefix
            {
                successfulUploads++;
            }
        }

        double successRate = (double)successfulUploads / testScenarios.Count * 100;
        
        Assert.True(successRate >= 80, $"Success rate was only {successRate}%");
        
        Assert.Equal(testScenarios.Count, virtualBucket.Count);
    }

    [Fact]
    public async Task S3Service_ValidationTest_ShouldSaveAndLogWithConsent() //funktionelle krav 11
    {
        var db = GetDatabaseContext();
        var virtualBucket = new Dictionary<string, byte[]>();
        var mockUploader = new Mock<IAmazonS3>();
        
        mockUploader.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
        .Callback<PutObjectRequest, CancellationToken>((request, token) => {
            using var ms = new MemoryStream();
            request.InputStream.CopyTo(ms);
            virtualBucket[request.Key] = ms.ToArray();
        })
        .ReturnsAsync(new PutObjectResponse { 
            HttpStatusCode = System.Net.HttpStatusCode.OK, 
            ChecksumSHA256 = "mock-hash" 
        });

        var mockConf = new Mock<IConfiguration>();
        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(mockConf.Object, fileService, consentService, mockUploader.Object, new Mock<IAmazonS3>().Object);

        var user = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User1", "password");
        var timeOfConsent = DateTime.UtcNow;
        
        var consentDto = new GiveConsentDto { 
            ConsentGiven = true, 
            TimeOfConsent = timeOfConsent 
        };

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Fil-indhold"));
        await s3Service.UploadFile(ms, consentDto, "cv.pdf", user, FileCategory.Cv);

        Assert.Single(virtualBucket);

        var consentEntry = await db.Consents
            .Include(c => c.File)
            .FirstOrDefaultAsync(c => c.UserId == user.Id);

        Assert.NotNull(consentEntry);
        Assert.Equal(timeOfConsent, consentEntry.TimeOfConsent);
        Assert.NotNull(consentEntry.File);
        Assert.False(consentEntry.ConsentRetracted);
    }

    [Fact]
    public async Task S3Service_UploadFile_WhenConsentIsMissing_ShouldThrowConsentNotGivenException()
    {
        var db = GetDatabaseContext();
        var virtualBucket = new Dictionary<string, byte[]>();
        var mockUploader = new Mock<IAmazonS3>();
        
        // Vi opsætter uploaderen, men den bør aldrig blive kaldt
        mockUploader.Setup(x => x.PutObjectAsync(It.IsAny<PutObjectRequest>(), It.IsAny<CancellationToken>()))
        .Callback<PutObjectRequest, CancellationToken>((request, token) => {
            virtualBucket[request.Key] = new byte[0];
        })
        .ReturnsAsync(new PutObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });

        var mockConf = new Mock<IConfiguration>();
        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(mockConf.Object, fileService, consentService, mockUploader.Object, new Mock<IAmazonS3>().Object);

        var user = new User(JwtLibrary.JwtRoles.User, "email@example.com", "Test User1", "password");
        
        // Unhappy path: ConsentGiven er sat til false
        var consentDto = new GiveConsentDto { 
            ConsentGiven = false, 
            TimeOfConsent = DateTime.UtcNow 
        };

        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Dette skal blokeres"));


        // 1. Verificer at den korrekte exception kastes
        await Assert.ThrowsAsync<ConsentNotGivenException>(async () => 
        {
            await s3Service.UploadFile(ms, consentDto, "forbidden.pdf", user, FileCategory.Cv);
        });

        // 2. Verificer blokerings-kravet: Ingen fil i S3
        Assert.Empty(virtualBucket);

        // 3. Verificer database-integritet: Ingen S3File record oprettet
        var fileExistsInDb = await db.S3Files.AnyAsync(f => f.UserId == user.Id);
        Assert.False(fileExistsInDb);

        // 4. Verificer database-integritet: Intet samtykke logget
        var consentExistsInDb = await db.Consents.AnyAsync(c => c.UserId == user.Id);
        Assert.False(consentExistsInDb);
    }

    [Fact]
    public async Task UserService_RetractConsent_ShouldDeleteFileAndMarkConsentAsRetracted() //funktionelle krav 12
    {
        // --- Arrange ---
        var db = GetDatabaseContext();
        var virtualBucket = new Dictionary<string, byte[]>();
        var mockUploader = new Mock<IAmazonS3>();
        
        mockUploader.Setup(x => x.ListObjectsV2Async(It.IsAny<ListObjectsV2Request>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListObjectsV2Request req, CancellationToken t) => {
                var response = new ListObjectsV2Response();
                var matches = virtualBucket.Keys.Where(k => k.StartsWith(req.Prefix))
                    .Select(k => new S3Object { Key = k }).ToList();
                // Bemærk: S3Object.Key kan kræve refleksion eller en wrapper i nogle mock-scenarier
                return response; 
            });

        mockUploader.Setup(x => x.DeleteObjectAsync(It.IsAny<DeleteObjectRequest>(), It.IsAny<CancellationToken>()))
            .Callback<DeleteObjectRequest, CancellationToken>((req, t) => {
                // DeleteObjectRequest har kun én Key egenskab
                virtualBucket.Remove(req.Key);
            })
            .ReturnsAsync(new DeleteObjectResponse { HttpStatusCode = System.Net.HttpStatusCode.OK });


        var mockConf = new Mock<IConfiguration>();
        mockConf.Setup(c => c["BackBlaze:KeyName"]).Returns("test-bucket");

        var consentService = new ConsentService(db);
        var fileService = new FileService(db, consentService);
        var s3Service = new S3StorageService(mockConf.Object, fileService, consentService, mockUploader.Object, new Mock<IAmazonS3>().Object);
        var userService = new UserService(db, s3Service, consentService, fileService);

        var user = new User(JwtLibrary.JwtRoles.User, "borger@danmark.dk", "Søren Sørensen", "kode123");
        await db.Users.AddAsync(user);
        
        // Simuler eksisterende filer i S3 og DB
        var fileId = Guid.NewGuid();
        var s3Key = $"users/{user.Id}/Cv/{fileId}";
        virtualBucket[s3Key] = new byte[100]; // Filen findes i S3
        
        var s3File = new S3File(user, "min_profil.pdf", s3Key, "hash", fileId);
        await db.S3Files.AddAsync(s3File);

        await consentService.GiveConsent(user, s3File, new GiveConsentDto { ConsentGiven = true, TimeOfConsent = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await s3Service.DeleteFileAsync(s3File.Id, user);

        Assert.False(virtualBucket.ContainsKey(s3Key), "Filen blev ikke fjernet fra S3.");

        var consent = await db.Consents.AsNoTracking().FirstAsync(c => c.FileId == fileId && c.UserId == user.Id);
        Assert.True(consent.ConsentRetracted, "Samtykket blev ikke trukket tilbage ved sletning.");

        var dbFile = await db.S3Files.AsNoTracking().FirstAsync(i=>i.Id == fileId);
        _output.WriteLine($"dbfile: {dbFile.FileName}");
        Assert.NotNull(dbFile);

        Assert.True(string.IsNullOrEmpty(dbFile.FileName) || dbFile.FileName == "Anonymiseret");
    }



}