using System.ClientModel;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenAI.Responses;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;
using OpenAiResponses.Api.Services;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = LicenseType.Community;

// Enable basic API discovery so the sample endpoints are easy to inspect manually.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OpenAI Responses API",
        Version = "v1",
        Description = "Minimal API for sending local files to the OpenAI Responses API and getting strict JSON output back."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});
builder.Services.AddProblemDetails();

// Allow OpenAI settings to come from configuration while still supporting env-var secrets locally.
builder.Services
    .AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.SectionName))
    .PostConfigure(options =>
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            options.ApiKey = builder.Configuration["OPENAI_API_KEY"]
                ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? string.Empty;
        }
    })
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), $"{OpenAIOptions.SectionName}:ApiKey or OPENAI_API_KEY must be configured.")
    .Validate(options => CurrencyCodeHelper.IsIso4217Like(options.PricingCurrency), $"{OpenAIOptions.SectionName}:PricingCurrency must be a three-letter ISO 4217 code.")
    .Validate(options => CurrencyCodeHelper.IsIso4217Like(options.DisplayCurrency), $"{OpenAIOptions.SectionName}:DisplayCurrency must be a three-letter ISO 4217 code.")
    .Validate(options => options.Models.Count > 0, $"{OpenAIOptions.SectionName}:Models must contain at least one configured entry.")
    .Validate(options => options.Models.All(model => model.Key > 0 && !string.IsNullOrWhiteSpace(model.Value.Id)), $"{OpenAIOptions.SectionName}:Models entries must use positive numeric keys and non-empty Id values.")
    .Validate(options => IsVisionModelSelectionConfigured(options, options.Model), $"{OpenAIOptions.SectionName}:Model must point to a configured vision-capable model entry.")
    .Validate(options => !options.Phases.CompanyContext.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.CompanyContext.Model.Value), $"{OpenAIOptions.SectionName}:Phases:CompanyContext:Model must point to a configured vision-capable model entry.")
    .Validate(options => !options.Phases.Requirements.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.Requirements.Model.Value), $"{OpenAIOptions.SectionName}:Phases:Requirements:Model must point to a configured vision-capable model entry.")
    .Validate(options => !options.Phases.CandidateEvidence.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.CandidateEvidence.Model.Value), $"{OpenAIOptions.SectionName}:Phases:CandidateEvidence:Model must point to a configured vision-capable model entry.")
    .Validate(options => !options.Phases.Matching.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.Matching.Model.Value), $"{OpenAIOptions.SectionName}:Phases:Matching:Model must point to a configured vision-capable model entry.")
    .Validate(options => !options.Phases.ApplicationGeneration.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.ApplicationGeneration.Model.Value), $"{OpenAIOptions.SectionName}:Phases:ApplicationGeneration:Model must point to a configured vision-capable model entry.")
    .ValidateOnStart();

builder.Services
    .AddOptions<VerificationOptions>()
    .Bind(builder.Configuration.GetSection(VerificationOptions.SectionName))
    .ValidateOnStart();

builder.Services
    .AddOptions<SamplePipelineOptions>()
    .Bind(builder.Configuration.GetSection(SamplePipelineOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CandidateRootPath), $"{SamplePipelineOptions.SectionName}:CandidateRootPath must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultCandidateDirectory), $"{SamplePipelineOptions.SectionName}:DefaultCandidateDirectory must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.PreferencesFileName), $"{SamplePipelineOptions.SectionName}:PreferencesFileName must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.JobListingsPath), $"{SamplePipelineOptions.SectionName}:JobListingsPath must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ParsingSchemasPath), $"{SamplePipelineOptions.SectionName}:ParsingSchemasPath must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ResultsPath), $"{SamplePipelineOptions.SectionName}:ResultsPath must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RunDirectoryPrefix), $"{SamplePipelineOptions.SectionName}:RunDirectoryPrefix must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.TemplatesPath), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:TemplatesPath must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.HtmlTemplateFileName), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:HtmlTemplateFileName must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.CssTemplateFileName), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:CssTemplateFileName must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.RenderedPdfFileName), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:RenderedPdfFileName must be configured.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.OutputDirectoryName), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:OutputDirectoryName must be configured.")
    .Validate(options => options.CoverLetterTemplate.EstimatedCharactersPerLine > 0, $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:EstimatedCharactersPerLine must be greater than zero.")
    .Validate(options => options.CoverLetterTemplate.MaxMainContentCharacters > 0, $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:MaxMainContentCharacters must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHttpClient("exchange-rate-api", client =>
{
    client.BaseAddress = new Uri("https://open.er-api.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
});

// The responses client is shared so all routes use the same configured API key.
builder.Services.AddSingleton<ResponsesClient>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    return new ResponsesClient(options.ApiKey);
});

// Register the services that power the staged sample pipeline and its repair/gate flow.
builder.Services.AddSingleton<IOpenAiResponsesService, OpenAiResponsesService>();
builder.Services.AddSingleton<ICompanyContextService, CompanyContextService>();
builder.Services.AddSingleton<IRequirementsParsingService, RequirementsParsingService>();
builder.Services.AddSingleton<IExchangeRateCacheService, ExchangeRateCacheService>();
builder.Services.AddSingleton<ICurrencyDisplayConversionService, CurrencyDisplayConversionService>();
builder.Services.AddSingleton<ICoverLetterTemplateRenderer, CoverLetterTemplateRenderer>();
builder.Services.AddSingleton<ICoverLetterPdfRenderer, CoverLetterPdfRenderer>();
builder.Services.AddSingleton<IVerificationOrchestrator, VerificationOrchestrator>();
builder.Services.AddSingleton<IDownstreamGateEvaluator, DownstreamGateEvaluator>();
builder.Services.AddSingleton<IRequirementsDeterministicRepairService, RequirementsDeterministicRepairService>();
builder.Services.AddSingleton<IMatchingDeterministicRepairService, MatchingDeterministicRepairService>();
builder.Services.AddSingleton<IApplicationGenerationDeterministicRepairService, ApplicationGenerationDeterministicRepairService>();
builder.Services.AddSingleton<ISampleLlmFlowService, SampleLlmFlowService>();
builder.Services.AddHostedService<ExchangeRateRefreshService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "OpenAI Responses API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseExceptionHandler();
if (!app.Configuration.GetValue("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}

// Return a ready-to-run request body so the strict-json endpoint can be tested quickly.
app.MapGet("/api/responses/sample-request", (IHostEnvironment environment, IOptions<SamplePipelineOptions> samplePipelineOptions) =>
{
    var sampleOptions = samplePipelineOptions.Value;
    var personDirectory = RepositoryRootResolver.ResolveRepositoryPath(
        app.Configuration,
        environment,
        Path.Combine(sampleOptions.CandidateRootPath, sampleOptions.DefaultCandidateDirectory));
    var jobDirectory = RepositoryRootResolver.ResolveRepositoryPath(app.Configuration, environment, sampleOptions.JobListingsPath);

    var personFiles = Directory.Exists(personDirectory)
        ? Directory.GetFiles(personDirectory)
            .Where(path => !string.Equals(Path.GetFileName(path), sampleOptions.PreferencesFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path)
            .ToArray()
        : [];

    var jobApplication = Directory.Exists(jobDirectory)
        ? Directory.GetFiles(jobDirectory)
            .OrderBy(path => path)
            .FirstOrDefault()
        : null;

    var sampleSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            candidateName = new { type = "string" },
            overallMatchScore = new { type = "number" },
            summary = new { type = "string" },
            strengths = new
            {
                type = "array",
                items = new { type = "string" }
            },
            concerns = new
            {
                type = "array",
                items = new { type = "string" }
            }
        },
        required = new[] { "candidateName", "overallMatchScore", "summary", "strengths", "concerns" },
        additionalProperties = false
    });

    return Results.Json(new StrictJsonResponseRequest
    {
        PersonFiles = personFiles.ToList(),
        JobApplication = jobApplication ?? string.Empty,
        OutputSchema = sampleSchema,
        Prompt = "Compare the person files to the job application and explain the match.",
        SchemaName = "candidate_job_match",
        SchemaDescription = "A strict candidate-to-job comparison result."
    });
})
.WithName("GetSampleStrictJsonRequest")
.WithSummary("Returns a ready-to-run example body for the generic strict-json route.")
.WithDescription(
    "Use this helper route to inspect a concrete example body for **/api/responses/strict-json**.\n\n"
    + "The returned JSON shows:\n"
    + "- which candidate files should go into **personFiles**\n"
    + "- which job posting should go into **jobApplication**\n"
    + "- where to place the task text in **prompt**\n"
    + "- where to place the output contract in **outputSchema**")
.Produces<StrictJsonResponseRequest>(StatusCodes.Status200OK);

// Expose the generic strict-json route used by the more specialized sample flows.
app.MapPost("/api/responses/strict-json", async (
    StrictJsonResponseRequest request,
    IOpenAiResponsesService openAiResponsesService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        token => openAiResponsesService.GenerateStrictJsonAsync(request, token),
        logger,
        cancellationToken);
})
.WithName("GenerateStrictJsonResponse")
.WithSummary("Runs one generic strict-JSON task against uploaded local files.")
.WithDescription(
    "Low-level route for technicians who want a custom schema-constrained OpenAI run.\n\n"
    + "Key fields:\n"
    + "- **personFiles**: local file paths to candidate-side source documents\n"
    + "- **jobApplication**: local file path to the target job posting\n"
    + "- **prompt**: the task the model should perform\n"
    + "- **outputSchema**: the exact JSON Schema the output must follow\n"
    + "- **model**: optional explicit model override\n\n"
    + "Use this route when you do not want the predefined sample pipeline behavior.")
.Accepts<StrictJsonResponseRequest>("application/json")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

// Keep each pipeline stage callable on its own so prompts and schemas can be debugged in isolation.
app.MapPost("/api/responses/sample/company-context", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunCompanyContextAsync,
        logger,
        cancellationToken);
})
.WithName("RunSampleCompanyContext")
.WithSummary("Builds company context for the default sample job posting and applicant profile.")
.WithDescription(
    "No request body is required.\n\n"
    + "The route uses:\n"
    + "- the configured default sample job posting\n"
    + "- the configured sample applicant profile\n\n"
    + "It then runs the CompanyContext phase with structured output and web search.")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/requirements", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunRequirementsParsingAsync,
        logger,
        cancellationToken);
})
.WithName("RunSampleRequirementsParsing")
.WithSummary("Parses requirements from the default sample job posting.")
.WithDescription(
    "No request body is required.\n\n"
    + "The route:\n"
    + "- reads the configured default sample job listing\n"
    + "- extracts structured requirements\n"
    + "- returns the requirements document used by later pipeline stages")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/requirements/upload", async (
    [FromForm] RequirementsUploadRequest request,
    IRequirementsParsingService requirementsParsingService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var tempDirectory = CreateTemporaryUploadDirectory();

    try
    {
        string? jobPostingFilePath = null;
        if (request.JobPostingFile is not null)
        {
            jobPostingFilePath = await SaveUploadedFileAsync(request.JobPostingFile, tempDirectory, cancellationToken);
        }

        var generationRequest = new RequirementsGenerationRequest
        {
            JobPostingText = request.JobPostingText,
            JobPostingFilePath = jobPostingFilePath
        };

        return await ExecuteJsonResponseAsync(
            async token => (await requirementsParsingService.GenerateRequirementsAsync(generationRequest, token)).OutputJson,
            logger,
            cancellationToken);
    }
    finally
    {
        DeleteTemporaryUploadDirectory(tempDirectory);
    }
})
.WithName("GenerateRequirementsFromUpload")
.WithSummary("Generates structured requirements from an uploaded or inline job posting.")
.WithDescription(
    "Use this route when another backend service already has the job posting bytes and needs the real requirements prompt/schema path from the llm-api.\n\n"
    + "Input fields:\n"
    + "- **jobPostingFile**: uploaded job posting that should be parsed directly\n"
    + "- **jobPostingText**: optional raw text fallback when the caller already extracted the posting upstream")
.Accepts<RequirementsUploadRequest>("multipart/form-data")
.DisableAntiforgery()
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/requirements/verify", async (
    RequirementsVerificationDirectRequest request,
    IVerificationOrchestrator verificationOrchestrator,
    IDownstreamGateEvaluator downstreamGateEvaluator,
    IHostEnvironment environment,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var schemaPath = RepositoryRootResolver.ResolveRepositoryPath(
        configuration,
        environment,
        Path.Combine("LLM", "AI Schemas", "LLM Parsing", "requirements_schema.json"));

    var verificationRequest = new StageVerificationRequest
    {
        Stage = VerificationStage.Requirements,
        DocumentId = request.DocumentId,
        DocumentJson = request.DocumentJson,
        OutputSchemaPath = schemaPath,
        ExpectedParsedFiles = string.IsNullOrWhiteSpace(request.JobPostingFileName) ? [] : [request.JobPostingFileName],
        AllowedCitationFiles = string.IsNullOrWhiteSpace(request.JobPostingFileName) ? [] : [request.JobPostingFileName],
        DisallowedCitationFiles = []
    };

    var verificationResult = await verificationOrchestrator.VerifyStageAsync(verificationRequest, cancellationToken);
    var gateResult = downstreamGateEvaluator.Evaluate(verificationRequest, verificationResult);

    var response = new StageVerificationResult
    {
        Stage = verificationResult.Stage,
        DocumentId = verificationResult.DocumentId,
        VerificationMode = "mechanical_with_gate",
        Status = verificationResult.Status,
        ApprovedForDownstream = verificationResult.ApprovedForDownstream && gateResult.ApprovedForDownstream,
        WarningCount = verificationResult.WarningCount,
        ErrorCount = verificationResult.ErrorCount,
        ArtifactPath = string.Empty,
        GateArtifactPath = string.Empty,
        Gate = gateResult,
        Findings = verificationResult.Findings
    };

    return Results.Ok(response);
})
.WithName("VerifyRequirementsDocument")
.WithSummary("Runs mechanical verification and gate evaluation for one requirements document.")
.WithDescription(
    "This route lets another backend service reuse the same requirements verification and downstream gate rules as the sample pipeline, without running the full sample corpus flow.")
.Accepts<RequirementsVerificationDirectRequest>("application/json")
.Produces<StageVerificationResult>(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status500InternalServerError);

app.MapPost("/api/responses/company-context", async (
    CompanyContextDirectRequest request,
    ICompanyContextService companyContextService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var generationRequest = new CompanyContextGenerationRequest
    {
        CompanyName = request.CompanyName,
        JobPostingText = request.JobPostingText,
        ApplicantProfileText = request.ApplicantProfileText,
        ApplicantAddressHint = request.ApplicantAddressHint
    };

    return await ExecuteJsonResponseAsync(
        async token => (await companyContextService.GenerateCompanyContextAsync(generationRequest, token)).OutputJson,
        logger,
        cancellationToken);
})
.WithName("GenerateCompanyContext")
.WithSummary("Generates standalone company context from a company name and/or raw input text.")
.WithDescription(
    "Use this route when another service wants CompanyContext without file upload.\n\n"
    + "Input fields:\n"
    + "- **companyName**: direct employer lookup key when the caller already knows the company\n"
    + "- **jobPostingText**: raw job-ad text when the employer or workplace must be inferred from content\n"
    + "- **applicantProfileText**: applicant-side profile text, mainly used for address and commute context\n"
    + "- **applicantAddressHint**: explicit address fallback when the applicant address is not obvious from the profile text\n\n"
    + "Send only the fields you actually have. The phase can work with a partial input set.")
.Accepts<CompanyContextDirectRequest>("application/json")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/company-context/upload", async (
    [FromForm] CompanyContextUploadRequest request,
    ICompanyContextService companyContextService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var tempDirectory = CreateTemporaryUploadDirectory();

    try
    {
        string? jobPostingFilePath = null;
        var uploadedJobPosting = request.JobPostingFile;
        if (uploadedJobPosting is not null)
        {
            jobPostingFilePath = await SaveUploadedFileAsync(uploadedJobPosting, tempDirectory, cancellationToken);
        }

        var applicantProfileFilePaths = new List<string>();
        foreach (var uploadedApplicantProfile in request.ApplicantProfileFiles ?? [])
        {
            applicantProfileFilePaths.Add(await SaveUploadedFileAsync(uploadedApplicantProfile, tempDirectory, cancellationToken));
        }

        var generationRequest = new CompanyContextGenerationRequest
        {
            CompanyName = request.CompanyName,
            JobPostingText = request.JobPostingText,
            JobPostingFilePath = jobPostingFilePath,
            ApplicantProfileText = request.ApplicantProfileText,
            ApplicantAddressHint = request.ApplicantAddressHint,
            ApplicantProfileFilePaths = applicantProfileFilePaths
        };

        return await ExecuteJsonResponseAsync(
            async token => (await companyContextService.GenerateCompanyContextAsync(generationRequest, token)).OutputJson,
            logger,
            cancellationToken);
    }
    finally
    {
        DeleteTemporaryUploadDirectory(tempDirectory);
    }
})
.WithName("GenerateCompanyContextFromUpload")
.WithSummary("Generates standalone company context from uploaded job-posting and applicant-profile files.")
.WithDescription(
    "Use this route when another service wants CompanyContext from **multipart/form-data**.\n\n"
    + "Form fields:\n"
    + "- **jobPostingFile**: upload the original job ad file\n"
    + "- **applicantProfileFiles**: upload CV, profile, or similar applicant documents\n"
    + "- **companyName**: optional direct employer lookup key\n"
    + "- **jobPostingText**: optional text fallback or supplement to **jobPostingFile**\n"
    + "- **applicantProfileText**: optional text fallback or supplement to **applicantProfileFiles**\n"
    + "- **applicantAddressHint**: optional explicit commute-distance hint\n\n"
    + "This route is the best fit when the calling system already has files rather than extracted text.")
.Accepts<CompanyContextUploadRequest>("multipart/form-data")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway)
.DisableAntiforgery();

app.MapPost("/api/responses/sample/candidate-evidence", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunCandidateEvidenceAsync,
        logger,
        cancellationToken);
})
.WithName("RunSampleCandidateEvidence")
.WithSummary("Builds candidate evidence from the default sample profile against the parsed sample requirements.")
.WithDescription(
    "No request body is required.\n\n"
    + "The route:\n"
    + "- parses requirements from the default sample job posting\n"
    + "- reads the configured sample candidate files\n"
    + "- extracts structured applicant evidence linked to the parsed requirements")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/matching", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunMatchingAsync,
        logger,
        cancellationToken);
})
.WithName("RunSampleMatching")
.WithSummary("Builds requirement-to-evidence matching for the default sample data.")
.WithDescription(
    "No request body is required.\n\n"
    + "The route first runs:\n"
    + "- sample requirements parsing\n"
    + "- sample candidate-evidence extraction\n\n"
    + "It then returns structured matching output that maps requirement ids to evidence ids and match verdicts.")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/pipeline", async (
    SamplePipelineSelectionRequest? request,
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        ct => sampleLlmFlowService.RunPipelineAsync(request, ct),
        logger,
        cancellationToken);
})
.WithName("RunSamplePipeline")
.WithSummary("Runs the full sample pipeline without verification metadata.")
.WithDescription(
    "Optional request body fields:\n"
    + "- **candidateNumber**: 1-based sample candidate selector, currently 1-2\n"
    + "- **jobPostingNumber**: 1-based sample job-posting selector, currently 1-5\n\n"
    + "The route runs the configured sample flow end to end and returns:\n"
    + "- the final application document\n\n"
    + "It does not include per-stage verification or gate metadata.")
.Accepts<SamplePipelineSelectionRequest>("application/json")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/pipeline-with-verification", async (
    SamplePipelineSelectionRequest? request,
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        ct => sampleLlmFlowService.RunPipelineWithVerificationAsync(request, ct),
        logger,
        cancellationToken);
})
.WithName("RunSamplePipelineWithVerification")
.WithSummary("Runs the sample pipeline with verification, matching repair, and downstream gates.")
.WithDescription(
    "Optional request body fields:\n"
    + "- **candidateNumber**: 1-based sample candidate selector, currently 1-2\n"
    + "- **jobPostingNumber**: 1-based sample job-posting selector, currently 1-5\n\n"
    + "Runs the sample pipeline with verification, repair handling, and downstream gate evaluation.\n\n"
    + "Important response fields:\n"
    + "- **candidateDirectory**: selected sample candidate directory\n"
    + "- **jobListingFileName**: selected sample job posting\n"
    + "- **pipelineStatus**: overall pipeline outcome\n"
    + "- **stoppedAfterStage**: the stage where the run stopped, if any\n"
    + "- **verification.recommendedAction**: high-level next-step recommendation\n"
    + "- **coverLetter.pdfArtifactPath**: generated one-page PDF artifact when rendering succeeded\n"
    + "- per-stage gate decisions and artifact paths\n\n"
    + "If a downstream gate fails, the run stops early and **applicationDocument** is null.")
.Accepts<SamplePipelineSelectionRequest>("application/json")
.Produces<PipelineWithVerificationResponse>(StatusCodes.Status200OK, contentType: "application/json")
.ProducesValidationProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/pipeline-with-verification/all-jobs", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunPipelineWithVerificationForAllJobListingsAsync,
        logger,
        cancellationToken);
})
.WithName("RunSamplePipelineWithVerificationForAllJobs")
.WithSummary("Runs the verified sample pipeline for every job listing in the configured sample job-listing directory.")
.WithDescription(
    "Runs the verified sample pipeline for every configured sample job listing.\n\n"
    + "Returns one compact summary per job listing with:\n"
    + "- pipeline status\n"
    + "- overall match level\n"
    + "- verdict counts\n"
    + "- whether an application was generated")
.Produces<MultiJobPipelineWithVerificationResponse>(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html")).ExcludeFromDescription();
app.MapGet("/", () => Results.Redirect("/swagger/index.html")).ExcludeFromDescription();

app.Run();

// Centralize exception-to-problem-details mapping so the route handlers stay focused on orchestration.
static async Task<IResult> ExecuteJsonResponseAsync(
    Func<CancellationToken, Task<string>> operation,
    ILogger logger,
    CancellationToken cancellationToken)
{
    try
    {
        var json = await operation(cancellationToken);
        return Results.Content(JsonSerializationDefaults.FormatJson(json), JsonSerializationDefaults.JsonUtf8ContentType);
    }
    catch (FileNotFoundException exception)
    {
        return Results.Problem(
            title: "Input file not found",
            detail: exception.Message,
            statusCode: StatusCodes.Status404NotFound);
    }
    catch (ArgumentException exception)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [exception.ParamName ?? "request"] = [exception.Message]
        });
    }
    catch (ClientResultException exception)
    {
        logger.LogError(exception, "The OpenAI request failed.");
        return Results.Problem(
            title: "OpenAI request failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException exception)
    {
        logger.LogError(exception, "The response could not be processed.");
        return Results.Problem(
            title: "Response processing failed",
            detail: exception.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
}

static bool IsVisionModelSelectionConfigured(OpenAIOptions options, int modelSelection)
{
    return options.Models.TryGetValue(modelSelection, out var model)
        && !string.IsNullOrWhiteSpace(model.Id)
        && model.SupportsVision;
}

static string CreateTemporaryUploadDirectory()
{
    var tempDirectory = Path.Combine(Path.GetTempPath(), "company-context-uploads", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    return tempDirectory;
}

static async Task<string> SaveUploadedFileAsync(IFormFile uploadedFile, string tempDirectory, CancellationToken cancellationToken)
{
    var safeFileName = string.IsNullOrWhiteSpace(uploadedFile.FileName)
        ? $"{Guid.NewGuid():N}.bin"
        : $"{Guid.NewGuid():N}_{Path.GetFileName(uploadedFile.FileName)}";
    var destinationPath = Path.Combine(tempDirectory, safeFileName);

    await using var stream = File.Create(destinationPath);
    await uploadedFile.CopyToAsync(stream, cancellationToken);

    return destinationPath;
}

static void DeleteTemporaryUploadDirectory(string tempDirectory)
{
    try
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
    catch
    {
        // Best-effort cleanup for transient upload files.
    }
}
