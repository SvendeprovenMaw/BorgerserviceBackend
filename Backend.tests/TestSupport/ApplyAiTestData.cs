using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using ApplyAI.Playwright;
using Backend.api.Database;
using Backend.api.Entities;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using JwtLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Backend.tests.TestSupport;

internal static class ApplyAiTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
        ],
        authenticationType: "Test"));
    }

    public static User CreateUser(Guid? id = null, string? email = null, string? username = null)
    {
        var resolvedId = id ?? Guid.NewGuid();
        return new User(
            JwtRoles.User,
            email ?? $"user-{resolvedId:N}@example.com",
            username ?? $"user-{resolvedId:N}",
            "hashed-password",
            resolvedId);
    }

    public static S3File CreateFile(
        User user,
        string fileName = "resume.pdf",
        Guid? id = null,
        DateTime? uploadTimeUtc = null,
        string? storageKey = null)
    {
        return new S3File(
            id ?? Guid.NewGuid(),
            user,
            fileName,
            storageKey ?? $"users/{user.Id:N}/CarreerDocuments/Cv/{Guid.NewGuid():N}_{fileName}",
            "sha256",
            uploadTimeUtc ?? new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc));
    }

    public static Consent CreateConsent(User user, S3File file, DateTime? consentedAtUtc = null)
    {
        return new Consent(user, file, consentedAtUtc ?? new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc));
    }

    public static Term CreateActiveTerm(User owner, bool active = true, string version = "v1")
    {
        return new Term(owner, "terms.pdf", $"terms/{Guid.NewGuid():N}.pdf", "terms-checksum", version, active);
    }

    public static JsonElement JsonElement(object? value)
    {
        return JsonSerializer.SerializeToElement(value ?? new { }, JsonOptions);
    }

    public static string Json(object? value)
    {
        return JsonSerializer.Serialize(value ?? new { }, JsonOptions);
    }

    public static ApplyAiStoredArtifact CreateStoredArtifact(
        Guid? artifactId = null,
        string? storageKey = null,
        string relativePath = "requirements.json",
        string displayName = "requirements.json",
        string mediaType = "application/json",
        string? checksum = "ABC123")
    {
        return new ApplyAiStoredArtifact(
            artifactId ?? Guid.NewGuid(),
            storageKey ?? $"users/test/Runs/2026-04-19/{Guid.NewGuid():N}/{relativePath}",
            relativePath,
            displayName,
            mediaType,
            checksum);
    }

    public static ApplyAiArtifactContentResponse CreateArtifactContent(
        string content,
        string mediaType = "application/json",
        string fileName = "artifact.json")
    {
        return new ApplyAiArtifactContentResponse(Encoding.UTF8.GetBytes(content), mediaType, fileName);
    }

    public static StructuredJsonGenerationResult CreateStructuredResult(object phaseOutput, string model = "gpt-5.4-mini")
    {
        return new StructuredJsonGenerationResult
        {
            OutputJson = Json(phaseOutput),
            Model = model,
            ResponseId = Guid.NewGuid().ToString("N"),
            RequestedDisplayCurrency = "DKK",
            DisplayCurrency = "DKK",
            TokenUsage = new LlmTokenUsage
            {
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                CachedInputTokens = 0,
                ReasoningTokens = 0,
            },
            Pricing = new LlmPricingSnapshot
            {
                Model = model,
                Currency = "USD",
                InputCostPerMillionTokens = 0.75m,
                OutputCostPerMillionTokens = 4.5m,
                PricingConfigured = true,
            },
            CurrencyExchange = new CurrencyExchangeRateQuote
            {
                ApiAvailability = CurrencyExchangeRateQuote.NotRequiredAvailability,
                SourceCurrency = "USD",
                TargetCurrency = "DKK",
                AppliedRate = 7.0m,
            },
            EstimatedCost = new LlmTokenCostSummary
            {
                Currency = "DKK",
                PricingConfigured = true,
                InputCost = 0.01m,
                OutputCost = 0.02m,
                TotalCost = 0.03m,
                RoundedUpTotalCost = "0.03",
                RoundedUpTotalCostNumeric = 0.03m,
            },
        };
    }

    public static GateEvaluationResult CreateGateResult(
        string stage,
        bool approvedForDownstream = true,
        string decision = "continue",
        IEnumerable<string>? blockingReasons = null,
        IEnumerable<string>? advisoryReasons = null)
    {
        return new GateEvaluationResult
        {
            Stage = stage,
            Decision = decision,
            ApprovedForDownstream = approvedForDownstream,
            SummaryDa = approvedForDownstream ? "Approved" : "Blocked",
            BlockingReasons = blockingReasons?.ToList() ?? [],
            AdvisoryReasons = advisoryReasons?.ToList() ?? [],
        };
    }

    public static StageVerificationResult CreateVerificationResult(
        VerificationStage stage,
        string documentId,
        bool approvedForDownstream = true,
        int warningCount = 0,
        int errorCount = 0,
        string status = "pass",
        GateEvaluationResult? gate = null,
        IEnumerable<VerificationFinding>? findings = null)
    {
        return new StageVerificationResult
        {
            Stage = stage.ToString(),
            DocumentId = documentId,
            Status = status,
            ApprovedForDownstream = approvedForDownstream,
            WarningCount = warningCount,
            ErrorCount = errorCount,
            Gate = gate ?? CreateGateResult(stage.ToString(), approvedForDownstream),
            Findings = findings?.ToList() ?? [],
        };
    }

    public static ApplyAiStageVerificationResult CreateStageVerificationResult(
        bool approvedForDownstream = true,
        int warningCount = 0,
        int errorCount = 0,
        string status = "pass",
        string? verificationJson = null,
        string? gateJson = null)
    {
        var gate = CreateGateResult("Requirements", approvedForDownstream, approvedForDownstream ? "continue" : "review");
        return new ApplyAiStageVerificationResult(
            verificationJson ?? Json(new { status, approvedForDownstream, warningCount, errorCount }),
            gateJson ?? Json(gate),
            approvedForDownstream,
            warningCount,
            errorCount,
            status);
    }

    public static CoverLetterTemplateRenderResult CreateTemplateRenderResult(
        string html = "<html><body>Rendered</body></html>",
        string css = "body { color: black; }",
        int maxMainContentCharacters = 1550)
    {
        return new CoverLetterTemplateRenderResult
        {
            HtmlDocument = html,
            StylesheetText = css,
            MainContentCharacterCount = 1200,
            MainContentBudgetUsage = 1200,
            MaxMainContentCharacters = maxMainContentCharacters,
            ExplicitLineBreakCount = 0,
            ParagraphBreakCount = 3,
            EstimatedCharactersPerLine = 72,
            WithinMainContentLimit = true,
            MissingFields = [],
            Warnings = [],
        };
    }

    public static CoverLetterPdfRenderResult CreatePdfRenderResult(byte[]? pdfDocument = null)
    {
        return new CoverLetterPdfRenderResult
        {
            PdfDocument = pdfDocument ?? Encoding.UTF8.GetBytes("%PDF-1.7"),
            PageCount = 1,
            WithinSinglePageLimit = true,
        };
    }

    public static RenderedPdfDocument CreateRenderedPdfDocument(
        byte[]? content = null,
        string fileName = "rendered-job-posting.pdf",
        string contentType = "application/pdf",
        string? pageTitle = "Rendered Job Posting")
    {
        return new RenderedPdfDocument(content ?? Encoding.UTF8.GetBytes("rendered-pdf"), fileName, contentType, pageTitle);
    }

    public static IFormFile CreateFormFile(
        string fileName = "job-posting.pdf",
        string contentType = "application/pdf",
        string content = "pdf-content")
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "JobPostingFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    public static string WrapPhaseDocument(object phaseOutput)
    {
        return Json(new
        {
            generatedAtUtc = "2026-04-19T12:00:00Z",
            workflowMode = "Auto",
            candidateFiles = Array.Empty<object>(),
            phaseInputs = new { },
            phaseOutput,
        });
    }
}

internal static class ApplyAiDbContextFactory
{
    public static ApplyAIDbContext CreateInMemory(string? databaseName = null)
    {
        var options = new DbContextOptionsBuilder<ApplyAIDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .Options;

        var context = new ApplyAIDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static SqliteApplyAiDbContextScope CreateSqliteScope()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<ApplyAIDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new ApplyAIDbContext(options);
        context.Database.EnsureCreated();
        return new SqliteApplyAiDbContextScope(connection, context);
    }
}

internal sealed class SqliteApplyAiDbContextScope : IDisposable, IAsyncDisposable
{
    public SqliteApplyAiDbContextScope(SqliteConnection connection, ApplyAIDbContext db)
    {
        Connection = connection;
        Db = db;
    }

    public SqliteConnection Connection { get; }

    public ApplyAIDbContext Db { get; }

    public void Dispose()
    {
        Db.Dispose();
        Connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "Backend.tests";

    public string ContentRootPath { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Backend.api"));

    public IFileProvider ContentRootFileProvider { get; set; }
        = new PhysicalFileProvider(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Backend.api")));
}

internal static class TestConfiguration
{
    public static IConfiguration Empty() => new ConfigurationBuilder().AddInMemoryCollection().Build();
}