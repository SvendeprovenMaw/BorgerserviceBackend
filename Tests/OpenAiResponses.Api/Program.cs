using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenAI.Responses;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;
using OpenAiResponses.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
    .Validate(options => !string.IsNullOrWhiteSpace(options.CoverLetterTemplate.OutputDirectoryName), $"{SamplePipelineOptions.SectionName}:CoverLetterTemplate:OutputDirectoryName must be configured.")
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
builder.Services.AddSingleton<IExchangeRateCacheService, ExchangeRateCacheService>();
builder.Services.AddSingleton<ICurrencyDisplayConversionService, CurrencyDisplayConversionService>();
builder.Services.AddSingleton<ICoverLetterTemplateRenderer, CoverLetterTemplateRenderer>();
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
.Accepts<StrictJsonResponseRequest>("application/json")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

// Keep each pipeline stage callable on its own so prompts and schemas can be debugged in isolation.
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
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

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
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/pipeline", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunPipelineAsync,
        logger,
        cancellationToken);
})
.WithName("RunSamplePipeline")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapPost("/api/responses/sample/pipeline-with-verification", async (
    ISampleLlmFlowService sampleLlmFlowService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    return await ExecuteJsonResponseAsync(
        sampleLlmFlowService.RunPipelineWithVerificationAsync,
        logger,
        cancellationToken);
})
.WithName("RunSamplePipelineWithVerification")
.WithSummary("Runs the sample pipeline with verification, matching repair, and downstream gates.")
.WithDescription("Returns PipelineStatus, StoppedAfterStage, Verification.RecommendedAction, per-stage Gate decisions, and GateArtifactPath values. If a stage fails its downstream gate, the run stops early and ApplicationDocument is null.")
.Produces<PipelineWithVerificationResponse>(StatusCodes.Status200OK, contentType: "application/json")
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
.WithDescription("Returns one compact summary per job listing, including pipeline status, overall match level, verdict counts, and whether an application was still generated.")
.Produces<MultiJobPipelineWithVerificationResponse>(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));

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
        return Results.Content(json, "application/json");
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
