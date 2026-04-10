using System.ClientModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using OpenAI.Responses;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;
using OpenAiResponses.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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
    .Validate(options => !string.IsNullOrWhiteSpace(options.Model), $"{OpenAIOptions.SectionName}:Model must be configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<ResponsesClient>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
    return new ResponsesClient(options.ApiKey);
});

builder.Services.AddSingleton<IOpenAiResponsesService, OpenAiResponsesService>();

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
app.UseHttpsRedirection();

app.MapGet("/api/responses/sample-request", (IHostEnvironment environment) =>
{
    var workspaceRoot = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    var personDirectory = Path.Combine(workspaceRoot, "BorgerserviceBackend", "TestData", "Borgere", "Borger1");
    var jobDirectory = Path.Combine(workspaceRoot, "BorgerserviceBackend", "TestData", "Opslag");

    var personFiles = Directory.Exists(personDirectory)
        ? Directory.GetFiles(personDirectory).OrderBy(path => path).ToArray()
        : [];

    var jobApplication = Directory.Exists(jobDirectory)
        ? Directory.GetFiles(jobDirectory).OrderBy(path => path).FirstOrDefault()
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

app.MapPost("/api/responses/strict-json", async (
    StrictJsonResponseRequest request,
    IOpenAiResponsesService openAiResponsesService,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        var json = await openAiResponsesService.GenerateStrictJsonAsync(request, cancellationToken);
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
})
.WithName("GenerateStrictJsonResponse")
.Accepts<StrictJsonResponseRequest>("application/json")
.Produces(StatusCodes.Status200OK, contentType: "application/json")
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status404NotFound)
.ProducesProblem(StatusCodes.Status500InternalServerError)
.ProducesProblem(StatusCodes.Status502BadGateway);

app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html"));
app.MapGet("/", () => Results.Redirect("/swagger/index.html"));

app.Run();
