using ApplyAI.Playwright;
using Backend.api.Services.ApplyAIService.LlmRuntime.Options;
using Backend.api.Services.ApplyAIService.LlmRuntime.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using QuestPDF.Infrastructure;

namespace Backend.api.Services.ApplyAIService
{
    public static class ApplyAIServiceCollectionExtensions
    {
        public static IServiceCollection AddApplyAiServiceModule(this IServiceCollection services, IConfiguration configuration)
        {
            services
                .AddOptions<OpenAIOptions>()
                .Bind(configuration.GetSection(OpenAIOptions.SectionName))
                .PostConfigure(options =>
                {
                    if (string.IsNullOrWhiteSpace(options.ApiKey))
                    {
                        options.ApiKey = configuration[$"{OpenAIOptions.SectionName}:ApiKey"]
                            ?? configuration["OpenAi:SecretKey"]
                            ?? configuration["OPENAI_API_KEY"]
                            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                            ?? string.Empty;
                    }

                    options.Model = options.Model <= 0 ? 1 : options.Model;
                    options.PricingCurrency = string.IsNullOrWhiteSpace(options.PricingCurrency) ? "USD" : options.PricingCurrency;
                    options.DisplayCurrency = string.IsNullOrWhiteSpace(options.DisplayCurrency) ? "DKK" : options.DisplayCurrency;

                    if (options.Models.Count == 0)
                    {
                        options.Models = new Dictionary<int, OpenAIModelCatalogEntry>
                        {
                            [1] = new()
                            {
                                Id = "gpt-5.4-mini",
                                SupportsVision = true,
                                InputCostPerMillionTokens = 0.75m,
                                CachedInputCostPerMillionTokens = 0.075m,
                                OutputCostPerMillionTokens = 4.5m
                            },
                            [2] = new()
                            {
                                Id = "gpt-5.4",
                                SupportsVision = true,
                                InputCostPerMillionTokens = 2.5m,
                                CachedInputCostPerMillionTokens = 0.25m,
                                OutputCostPerMillionTokens = 15m
                            },
                            [3] = new()
                            {
                                Id = "gpt-5.4-nano",
                                SupportsVision = true,
                                InputCostPerMillionTokens = 0.2m,
                                CachedInputCostPerMillionTokens = 0.02m,
                                OutputCostPerMillionTokens = 1.25m
                            }
                        };
                    }

                    options.Phases.CompanyContext.Model ??= options.Model;
                    options.Phases.Requirements.Model ??= options.Model;
                    options.Phases.CandidateEvidence.Model ??= options.Model;
                    options.Phases.Matching.Model ??= options.Model;
                    options.Phases.ApplicationGeneration.Model ??= options.Model;
                })
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), $"{OpenAIOptions.SectionName}:ApiKey or OPENAI_API_KEY must be configured.")
                .Validate(options => options.Models.Count > 0, $"{OpenAIOptions.SectionName}:Models must contain at least one configured entry.")
                .Validate(options => IsVisionModelSelectionConfigured(options, options.Model), $"{OpenAIOptions.SectionName}:Model must point to a configured vision-capable model entry.")
                .Validate(options => !options.Phases.CompanyContext.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.CompanyContext.Model.Value), $"{OpenAIOptions.SectionName}:Phases:CompanyContext:Model must point to a configured vision-capable model entry.")
                .Validate(options => !options.Phases.Requirements.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.Requirements.Model.Value), $"{OpenAIOptions.SectionName}:Phases:Requirements:Model must point to a configured vision-capable model entry.")
                .Validate(options => !options.Phases.CandidateEvidence.Model.HasValue || IsVisionModelSelectionConfigured(options, options.Phases.CandidateEvidence.Model.Value), $"{OpenAIOptions.SectionName}:Phases:CandidateEvidence:Model must point to a configured vision-capable model entry.")
                .Validate(options => !options.Phases.Matching.Model.HasValue || IsModelSelectionConfigured(options, options.Phases.Matching.Model.Value), $"{OpenAIOptions.SectionName}:Phases:Matching:Model must point to a configured model entry.")
                .Validate(options => !options.Phases.ApplicationGeneration.Model.HasValue || IsModelSelectionConfigured(options, options.Phases.ApplicationGeneration.Model.Value), $"{OpenAIOptions.SectionName}:Phases:ApplicationGeneration:Model must point to a configured model entry.")
                .ValidateOnStart();

            services
                .AddOptions<VerificationOptions>()
                .Bind(configuration.GetSection(VerificationOptions.SectionName))
                .ValidateOnStart();

            services.AddSingleton<ResponsesClient>(serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenAIOptions>>().Value;
                return new ResponsesClient(options.ApiKey);
            });

            QuestPDF.Settings.License = LicenseType.Community;

            services.AddSingleton<IOpenAiResponsesService, OpenAiResponsesService>();
            services.AddSingleton<IApplicationGenerationService, ApplicationGenerationService>();
            services.AddSingleton<ICandidateEvidenceService, CandidateEvidenceService>();
            services.AddSingleton<ICompanyContextService, CompanyContextService>();
            services.AddSingleton<ICoverLetterPdfRenderer, CoverLetterPdfRenderer>();
            services.AddSingleton<ICoverLetterTemplateRenderer, CoverLetterTemplateRenderer>();
            services.AddSingleton<IMatchingService, MatchingService>();
            services.AddSingleton<IRequirementsParsingService, RequirementsParsingService>();
            services.AddSingleton<IVerificationOrchestrator, VerificationOrchestrator>();
            services.AddSingleton<IDownstreamGateEvaluator, DownstreamGateEvaluator>();
            services.AddSingleton<ApplyAiExecutionQueue>();
            services.AddSingleton<IApplyAiExecutionQueue>(serviceProvider => serviceProvider.GetRequiredService<ApplyAiExecutionQueue>());
            services.AddSingleton<IHostedService>(serviceProvider => serviceProvider.GetRequiredService<ApplyAiExecutionQueue>());
            services.AddSingleton<IApplyAiStageOneRuntime, ApplyAiStageOneRuntime>();
            services.AddScoped<IApplyAiArtifactStorageService, ApplyAiArtifactStorageService>();
            services.AddScoped<IJobPostingPdfRenderer, PlaywrightJobPostingPdfRenderer>();
            services.AddScoped<ApplyAiJobStore>();
            services.AddScoped<ApplyAIService>();
            services.AddScoped<IApplyAIService>(serviceProvider => serviceProvider.GetRequiredService<ApplyAIService>());
            services.AddScoped<IApplyAiJobExecutionService>(serviceProvider => serviceProvider.GetRequiredService<ApplyAIService>());
            return services;
        }

        private static bool IsVisionModelSelectionConfigured(OpenAIOptions options, int modelSelection)
        {
            return options.Models.TryGetValue(modelSelection, out var model)
                && !string.IsNullOrWhiteSpace(model.Id)
                && model.SupportsVision;
        }

        private static bool IsModelSelectionConfigured(OpenAIOptions options, int modelSelection)
        {
            return options.Models.TryGetValue(modelSelection, out var model)
                && !string.IsNullOrWhiteSpace(model.Id);
        }
    }
}