using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Orchestrates the sample four-stage pipeline, including verification, repair, advisory output, and artifact persistence.
/// </summary>
public sealed class SampleLlmFlowService : ISampleLlmFlowService
{
    private const string SampleCandidateDirectory = "Borger1";
    private const string PreferencesFileName = "Preferences.json";
    private static readonly JsonSerializerOptions SavedJsonOptions = new() { WriteIndented = true };
    private static readonly Lock ResultsDirectoryLock = new();

    private readonly IHostEnvironment _environment;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly IVerificationOrchestrator _verificationOrchestrator;
    private readonly IDownstreamGateEvaluator _downstreamGateEvaluator;
    private readonly IRequirementsDeterministicRepairService _requirementsDeterministicRepairService;
    private readonly IMatchingDeterministicRepairService _matchingDeterministicRepairService;
    private readonly IApplicationGenerationDeterministicRepairService _applicationGenerationDeterministicRepairService;
    private readonly ILogger<SampleLlmFlowService> _logger;
    private readonly OpenAIOptions _openAiOptions;
    private readonly VerificationOptions _verificationOptions;

    public SampleLlmFlowService(
        IHostEnvironment environment,
        IOpenAiResponsesService openAiResponsesService,
        IVerificationOrchestrator verificationOrchestrator,
        IDownstreamGateEvaluator downstreamGateEvaluator,
        IOptions<OpenAIOptions> openAiOptions,
        IRequirementsDeterministicRepairService requirementsDeterministicRepairService,
        IMatchingDeterministicRepairService matchingDeterministicRepairService,
        IApplicationGenerationDeterministicRepairService applicationGenerationDeterministicRepairService,
        IOptions<VerificationOptions> verificationOptions,
        ILogger<SampleLlmFlowService> logger)
    {
        _environment = environment;
        _openAiResponsesService = openAiResponsesService;
        _verificationOrchestrator = verificationOrchestrator;
        _downstreamGateEvaluator = downstreamGateEvaluator;
        _openAiOptions = openAiOptions.Value;
        _requirementsDeterministicRepairService = requirementsDeterministicRepairService;
        _matchingDeterministicRepairService = matchingDeterministicRepairService;
        _applicationGenerationDeterministicRepairService = applicationGenerationDeterministicRepairService;
        _verificationOptions = verificationOptions.Value;
        _logger = logger;
    }

    public Task<string> RunRequirementsParsingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        return RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
    }

    public async Task<string> RunCandidateEvidenceAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));

        return await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);
    }

    public async Task<string> RunMatchingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceJson = await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);

        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        return await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            regenerationFeedbackJson: null,
            cancellationToken);
    }

    public async Task<string> RunPipelineAsync(CancellationToken cancellationToken = default)
    {
        var executionResult = await ExecutePipelineAsync(includeVerification: false, cancellationToken);
        return executionResult.ApplicationJson ?? throw new InvalidOperationException("Pipeline execution did not produce an application document.");
    }

    public async Task<string> RunPipelineWithVerificationAsync(CancellationToken cancellationToken = default)
    {
        var executionResult = await ExecutePipelineAsync(includeVerification: true, cancellationToken);
        var response = new PipelineWithVerificationResponse
        {
            RunDirectory = executionResult.RunDirectoryRelativePath,
            PipelineStatus = executionResult.VerificationSummary?.PipelineStatus ?? "completed",
            StoppedAfterStage = executionResult.VerificationSummary?.StoppedAfterStage,
            ApplicationDocument = executionResult.ApplicationJson is null ? null : ParseJsonElement(executionResult.ApplicationJson),
            FitAdvisory = executionResult.FitAdvisory,
            Verification = executionResult.VerificationSummary ?? new PipelineVerificationSummary
            {
                VerificationMode = "mechanical_with_gate",
                PipelineStatus = "completed",
                RecommendedAction = "continue",
                Status = "pass",
                ApprovedForDownstream = true
            }
        };

        return JsonSerializer.Serialize(response, SavedJsonOptions);
    }

    public async Task<string> RunPipelineWithVerificationForAllJobListingsAsync(CancellationToken cancellationToken = default)
    {
        var jobApplications = GetSampleJobApplications();
        var jobSummaries = new List<JobListingPipelineRunSummary>();

        foreach (var jobApplication in jobApplications)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "Starting verified sample pipeline run for job listing {JobListingFile}.",
                Path.GetFileName(jobApplication));

            var executionResult = await ExecutePipelineAsync(
                includeVerification: true,
                cancellationToken,
                selectedJobApplicationPath: jobApplication);

            jobSummaries.Add(BuildJobListingPipelineRunSummary(
                jobListingFileName: Path.GetFileName(jobApplication),
                executionResult: executionResult));
        }

        var response = new MultiJobPipelineWithVerificationResponse
        {
            CandidateDirectory = SampleCandidateDirectory,
            JobListingCount = jobSummaries.Count,
            Jobs = jobSummaries
        };

        return JsonSerializer.Serialize(response, SavedJsonOptions);
    }

    // The pipeline intentionally runs stage by stage so each output can be saved, verified, repaired, and reused.
    private async Task<PipelineExecutionResult> ExecutePipelineAsync(
        bool includeVerification,
        CancellationToken cancellationToken,
        string? selectedJobApplicationPath = null)
    {
        var sampleData = GetSampleData(selectedJobApplicationPath);
        var fitStrategy = await LoadFitStrategyPreferencesAsync(sampleData.PreferencesFilePath, cancellationToken);
        var jobApplicationFileName = Path.GetFileName(sampleData.JobApplication);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        var matchingDocumentId = BuildDocumentId(
            "matching",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var applicationDocumentId = BuildDocumentId(
            "application",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var runDirectory = CreateNextRunDirectory();
        var runDirectoryRelativePath = Path.GetRelativePath(GetRepositoryRoot(), runDirectory);
        var stageVerificationResults = new List<StageVerificationResult>();
        PipelineVerificationSummary? verificationSummary = null;

        _logger.LogInformation("Pipeline run directory created at {RunDirectory}.", runDirectoryRelativePath);

        _logger.LogInformation(
            "Flow 1/4 requirements parsing: sending job application {JobApplicationFile} to OpenAI model {Model}.",
            Path.GetFileName(sampleData.JobApplication),
            _openAiOptions.Model);

        var requirementsJson = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        await SaveJsonResultAsync(runDirectory, "requirements.json", requirementsJson, cancellationToken);

        if (includeVerification)
        {
            // Requirements quality is a hard prerequisite because all later IDs and citations depend on it.
            stageVerificationResults.Add(await VerifyAndPersistStageAsync(
                request: CreateRequirementsVerificationRequest(requirementsJson, requirementsDocumentId, sampleData.JobApplication),
                runDirectory,
                artifactFileName: "requirements_verification.json",
                gateArtifactFileName: "requirements_gate.json",
                cancellationToken));

            if (ShouldAttemptRequirementsRecovery(stageVerificationResults[^1]))
            {
                var requirementsRecoveryOutcome = await RepairRequirementsAsync(
                    runDirectory: runDirectory,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    jobApplicationPath: sampleData.JobApplication,
                    currentStageResult: stageVerificationResults[^1],
                    cancellationToken: cancellationToken);

                requirementsJson = requirementsRecoveryOutcome.RequirementsJson;
                stageVerificationResults[^1] = requirementsRecoveryOutcome.StageResult;
                await SaveJsonResultAsync(runDirectory, "requirements.json", requirementsJson, cancellationToken);
            }

            if (!stageVerificationResults[^1].ApprovedForDownstream)
            {
                verificationSummary = BuildPipelineVerificationSummary(stageVerificationResults, completedAllStages: false);
                await SaveVerificationArtifactAsync(runDirectory, "pipeline_verification_summary.json", verificationSummary, cancellationToken);
                _logger.LogWarning("Pipeline stopped after stage {Stage} because the downstream gate was not approved.", stageVerificationResults[^1].Stage);

                var fitAdvisory = includeVerification
                    ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchingJson: null, applicationJson: null, verificationSummary, cancellationToken)
                    : null;

                return new PipelineExecutionResult(
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: null,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory);
            }
        }

        _logger.LogInformation("Flow 1/4 requirements parsing: OpenAI replied.");
        _logger.LogInformation("Flow 2/4 candidate evidence: started.");

        var candidateEvidenceJson = await RunCandidateEvidenceCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "candidate_evidence.json", candidateEvidenceJson, cancellationToken);

        if (includeVerification)
        {
            // Candidate evidence can continue with advisory quality signals, but hard integrity failures still stop the run.
            stageVerificationResults.Add(await VerifyAndPersistStageAsync(
                request: new StageVerificationRequest
                {
                    Stage = VerificationStage.CandidateEvidence,
                    DocumentId = candidateEvidenceDocumentId,
                    DocumentJson = candidateEvidenceJson,
                    OutputSchemaPath = GetParsingSchemaPath("candidate_evidence_schema.json"),
                    ExpectedParsedFiles = sampleData.PersonFiles.Select(Path.GetFileName).OfType<string>().ToList(),
                    AllowedCitationFiles = sampleData.PersonFiles.Select(Path.GetFileName).OfType<string>().ToList(),
                    DisallowedCitationFiles = [Path.GetFileName(sampleData.JobApplication)],
                    RequirementsDocumentJson = requirementsJson,
                    ExpectedRequirementsDocumentId = requirementsDocumentId
                },
                runDirectory,
                artifactFileName: "candidate_evidence_verification.json",
                gateArtifactFileName: "candidate_evidence_gate.json",
                cancellationToken));

            if (!stageVerificationResults[^1].ApprovedForDownstream)
            {
                verificationSummary = BuildPipelineVerificationSummary(stageVerificationResults, completedAllStages: false);
                await SaveVerificationArtifactAsync(runDirectory, "pipeline_verification_summary.json", verificationSummary, cancellationToken);
                _logger.LogWarning("Pipeline stopped after stage {Stage} because the downstream gate was not approved.", stageVerificationResults[^1].Stage);

                var fitAdvisory = includeVerification
                    ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchingJson: null, applicationJson: null, verificationSummary, cancellationToken)
                    : null;

                return new PipelineExecutionResult(
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: null,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory);
            }
        }

        _logger.LogInformation("Flow 2/4 candidate evidence: completed.");
        _logger.LogInformation("Flow 3/4 matching: started.");

        var matchJson = await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            regenerationFeedbackJson: null,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "matching.json", matchJson, cancellationToken);

        if (includeVerification)
        {
            // Matching is the main place where weak-fit runs stay alive through advisory output and conservative repair.
            stageVerificationResults.Add(await VerifyAndPersistStageAsync(
                request: CreateMatchingVerificationRequest(
                    matchingJson: matchJson,
                    matchingDocumentId: matchingDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId),
                runDirectory,
                artifactFileName: "matching_verification.json",
                gateArtifactFileName: "matching_gate.json",
                cancellationToken));

            if (ShouldAttemptMatchingRecovery(stageVerificationResults[^1]))
            {
                var matchingRecoveryOutcome = await RepairAndMaybeRegenerateMatchingAsync(
                    runDirectory: runDirectory,
                    matchingJson: matchJson,
                    matchingDocumentId: matchingDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId,
                    currentStageResult: stageVerificationResults[^1],
                    cancellationToken: cancellationToken);

                matchJson = matchingRecoveryOutcome.MatchingJson;
                stageVerificationResults[^1] = matchingRecoveryOutcome.StageResult;
                await SaveJsonResultAsync(runDirectory, "matching.json", matchJson, cancellationToken);
            }

            if (!stageVerificationResults[^1].ApprovedForDownstream)
            {
                verificationSummary = BuildPipelineVerificationSummary(stageVerificationResults, completedAllStages: false);
                await SaveVerificationArtifactAsync(runDirectory, "pipeline_verification_summary.json", verificationSummary, cancellationToken);
                _logger.LogWarning("Pipeline stopped after stage {Stage} because the downstream gate was not approved.", stageVerificationResults[^1].Stage);

                var fitAdvisory = includeVerification
                    ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchJson, applicationJson: null, verificationSummary, cancellationToken)
                    : null;

                return new PipelineExecutionResult(
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: matchJson,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory);
            }
        }

        _logger.LogInformation("Flow 3/4 matching: completed.");
        _logger.LogInformation("Flow 4/4 application generation: started.");

        var applicationJson = await RunApplicationGenerationCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            matchJson,
            matchingDocumentId,
            applicationDocumentId,
            cancellationToken);
        await SaveJsonResultAsync(runDirectory, "application_generation.json", applicationJson, cancellationToken);

        if (includeVerification)
        {
            // The final application is still treated as an integrity surface, so structural mistakes are repaired before completion.
            stageVerificationResults.Add(await VerifyAndPersistStageAsync(
                request: CreateApplicationGenerationVerificationRequest(
                    applicationJson: applicationJson,
                    applicationDocumentId: applicationDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId,
                    matchingJson: matchJson,
                    matchingDocumentId: matchingDocumentId),
                runDirectory,
                artifactFileName: "application_generation_verification.json",
                gateArtifactFileName: "application_generation_gate.json",
                cancellationToken));

            if (ShouldAttemptApplicationGenerationRecovery(stageVerificationResults[^1]))
            {
                var applicationRecoveryOutcome = await RepairApplicationGenerationAsync(
                    runDirectory: runDirectory,
                    applicationJson: applicationJson,
                    applicationDocumentId: applicationDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId,
                    matchingJson: matchJson,
                    matchingDocumentId: matchingDocumentId,
                    currentStageResult: stageVerificationResults[^1],
                    cancellationToken: cancellationToken);

                applicationJson = applicationRecoveryOutcome.ApplicationJson;
                stageVerificationResults[^1] = applicationRecoveryOutcome.StageResult;
                await SaveJsonResultAsync(runDirectory, "application_generation.json", applicationJson, cancellationToken);
            }

            verificationSummary = BuildPipelineVerificationSummary(stageVerificationResults, completedAllStages: true);
            await SaveVerificationArtifactAsync(runDirectory, "pipeline_verification_summary.json", verificationSummary, cancellationToken);

            if (!stageVerificationResults[^1].ApprovedForDownstream)
            {
                _logger.LogWarning("Pipeline completed all stages, but the downstream gate failed at {Stage}.", stageVerificationResults[^1].Stage);

                var fitAdvisory = includeVerification
                    ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchJson, applicationJson, verificationSummary, cancellationToken)
                    : null;

                return new PipelineExecutionResult(
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: matchJson,
                    ApplicationJson: applicationJson,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory);
            }
        }

        _logger.LogInformation("Flow 4/4 application generation: completed.");
        _logger.LogInformation("Pipeline results saved to {RunDirectory}.", runDirectoryRelativePath);

        var completedFitAdvisory = includeVerification
            ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchJson, applicationJson, verificationSummary, cancellationToken)
            : null;

        return new PipelineExecutionResult(
            JobListingFileName: jobApplicationFileName,
            RunDirectory: runDirectory,
            RunDirectoryRelativePath: runDirectoryRelativePath,
            MatchingJson: matchJson,
            ApplicationJson: applicationJson,
            VerificationSummary: verificationSummary,
            FitAdvisory: completedFitAdvisory);
    }

    private async Task<string> RunMatchingCoreAsync(
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string? regenerationFeedbackJson,
        CancellationToken cancellationToken)
    {
        var matchingAsset = await LoadPhaseAssetAsync(
            promptFileName: "matching.prompt",
            schemaFileName: "matching_schema.json",
            cancellationToken);

        var inputTexts = new List<StructuredTextInput>
        {
            new()
            {
                Label = "Krav-dokument ID",
                Content = requirementsDocumentId
            },
            new()
            {
                Label = "Krav-dokument JSON",
                Content = requirementsJson
            },
            new()
            {
                Label = "Kandidat-evidens dokument ID",
                Content = candidateEvidenceDocumentId
            },
            new()
            {
                Label = "Kandidat-evidens dokument JSON",
                Content = candidateEvidenceJson
            }
        };

        if (!string.IsNullOrWhiteSpace(regenerationFeedbackJson))
        {
            inputTexts.Add(new StructuredTextInput
            {
                Label = "Matching regeneration feedback JSON",
                Content = regenerationFeedbackJson
            });
        }

        var request = new StructuredJsonResponseRequest
        {
            Prompt = matchingAsset.Prompt,
            SchemaName = matchingAsset.SchemaName,
            SchemaDescription = matchingAsset.SchemaDescription,
            OutputSchema = matchingAsset.OutputSchema,
            InputTexts = inputTexts
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunApplicationGenerationCoreAsync(
        SampleDataContext sampleData,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string matchingJson,
        string matchingDocumentId,
        string applicationDocumentId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sampleData.PreferencesFilePath))
        {
            throw new FileNotFoundException(
                $"Application generation preferences file was not found in sample candidate directory: {sampleData.CandidateDirectoryPath}",
                sampleData.CandidateDirectoryPath);
        }

        var applicationAsset = await LoadPhaseAssetAsync(
            promptFileName: "application_generation.prompt",
            schemaFileName: "application_generation_schema.json",
            cancellationToken);

        var preferencesJson = await ReadRequiredTextAsync(sampleData.PreferencesFilePath, cancellationToken);
        var request = new StructuredJsonResponseRequest
        {
            Prompt = applicationAsset.Prompt,
            SchemaName = applicationAsset.SchemaName,
            SchemaDescription = applicationAsset.SchemaDescription,
            OutputSchema = applicationAsset.OutputSchema,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Application document ID",
                    Content = applicationDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document ID",
                    Content = requirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Requirements document JSON",
                    Content = requirementsJson
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document ID",
                    Content = candidateEvidenceDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Candidate evidence document JSON",
                    Content = candidateEvidenceJson
                },
                new StructuredTextInput
                {
                    Label = "Matching document ID",
                    Content = matchingDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Matching document JSON",
                    Content = matchingJson
                },
                new StructuredTextInput
                {
                    Label = "Application generation preferences JSON",
                    Content = preferencesJson
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunRequirementsParsingCoreAsync(SampleDataContext sampleData, CancellationToken cancellationToken)
    {
        var requirementsAsset = await LoadPhaseAssetAsync(
            promptFileName: "requirements.prompt",
            schemaFileName: "requirements_schema.json",
            cancellationToken);

        var request = new StructuredJsonResponseRequest
        {
            Prompt = requirementsAsset.Prompt,
            SchemaName = requirementsAsset.SchemaName,
            SchemaDescription = requirementsAsset.SchemaDescription,
            OutputSchema = requirementsAsset.OutputSchema,
            InputFiles =
            [
                new StructuredFileInput
                {
                    Label = "Job application file",
                    FilePath = sampleData.JobApplication
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    private async Task<string> RunCandidateEvidenceCoreAsync(
        SampleDataContext sampleData,
        string requirementsJson,
        string requirementsDocumentId,
        CancellationToken cancellationToken)
    {
        var candidateEvidenceAsset = await LoadPhaseAssetAsync(
            promptFileName: "candidate_evidence.prompt",
            schemaFileName: "candidate_evidence_schema.json",
            cancellationToken);

        var request = new StructuredJsonResponseRequest
        {
            Prompt = candidateEvidenceAsset.Prompt,
            SchemaName = candidateEvidenceAsset.SchemaName,
            SchemaDescription = candidateEvidenceAsset.SchemaDescription,
            OutputSchema = candidateEvidenceAsset.OutputSchema,
            InputTexts =
            [
                new StructuredTextInput
                {
                    Label = "Krav-dokument ID",
                    Content = requirementsDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Krav-dokument JSON",
                    Content = requirementsJson
                }
            ],
            InputFiles = sampleData.PersonFiles
                .Select(path => new StructuredFileInput
                {
                    Label = "Kandidatfil",
                    FilePath = path
                })
                .ToList()
        };

        return await _openAiResponsesService.GenerateStructuredJsonAsync(request, cancellationToken);
    }

    /// <summary>
    /// Loads the phase prompt and parsing schema from the shared LLM asset folders.
    /// </summary>
    private async Task<FlowAsset> LoadPhaseAssetAsync(string promptFileName, string schemaFileName, CancellationToken cancellationToken)
    {
        var repositoryRoot = GetRepositoryRoot();
        var schemaContent = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "AI Schemas", "LLM Parsing", schemaFileName), cancellationToken);
        var combinedPrompt = await LoadCombinedPromptAsync(promptFileName, cancellationToken);

        using var schemaDocument = JsonDocument.Parse(schemaContent);
        var root = schemaDocument.RootElement;

        if (!root.TryGetProperty("schema", out var outputSchema))
        {
            throw new InvalidOperationException($"Schema file does not contain a 'schema' property: {schemaFileName}");
        }

        var schemaName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : null;

        return new FlowAsset(
            Prompt: combinedPrompt,
            SchemaName: string.IsNullOrWhiteSpace(schemaName) ? Path.GetFileNameWithoutExtension(schemaFileName) : schemaName,
            SchemaDescription: null,
            OutputSchema: outputSchema.Clone());
    }

            /// <summary>
            /// Prepends the shared base prompt so every phase inherits the same high-level operating rules.
            /// </summary>
    private async Task<string> LoadCombinedPromptAsync(string promptFileName, CancellationToken cancellationToken)
    {
        var repositoryRoot = GetRepositoryRoot();
        var basePrompt = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "Prompts", "base.prompt"), cancellationToken);
        var phasePrompt = await ReadRequiredTextAsync(Path.Combine(repositoryRoot, "LLM", "Prompts", promptFileName), cancellationToken);

        return string.Join(Environment.NewLine + Environment.NewLine, basePrompt.Trim(), phasePrompt.Trim());
    }

    private IReadOnlyList<string> GetSampleJobApplications()
    {
        var jobDirectory = Path.Combine(GetRepositoryRoot(), "TestData", "Opslag");

        if (!Directory.Exists(jobDirectory))
        {
            throw new FileNotFoundException($"Sample job directory was not found: {jobDirectory}", jobDirectory);
        }

        var jobApplications = Directory.GetFiles(jobDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (jobApplications.Length == 0)
        {
            throw new FileNotFoundException($"No job application files were found in: {jobDirectory}", jobDirectory);
        }

        return jobApplications;
    }

    /// <summary>
    /// Resolves the default sample candidate plus either the default or a caller-selected job posting.
    /// </summary>
    private SampleDataContext GetSampleData(string? selectedJobApplicationPath = null)
    {
        var repositoryRoot = GetRepositoryRoot();
        var personDirectory = Path.Combine(repositoryRoot, "TestData", "Borgere", SampleCandidateDirectory);
        var jobDirectory = Path.Combine(repositoryRoot, "TestData", "Opslag");

        if (!Directory.Exists(personDirectory))
        {
            throw new FileNotFoundException($"Sample candidate directory was not found: {personDirectory}", personDirectory);
        }

        if (!Directory.Exists(jobDirectory))
        {
            throw new FileNotFoundException($"Sample job directory was not found: {jobDirectory}", jobDirectory);
        }

        var personFiles = Directory.GetFiles(personDirectory)
            .Where(path => !string.Equals(Path.GetFileName(path), PreferencesFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var preferencesFilePath = Directory.GetFiles(personDirectory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), PreferencesFileName, StringComparison.OrdinalIgnoreCase));

        if (personFiles.Length == 0)
        {
            throw new FileNotFoundException($"No candidate files were found in: {personDirectory}", personDirectory);
        }

        var jobApplications = GetSampleJobApplications();
        var jobApplication = string.IsNullOrWhiteSpace(selectedJobApplicationPath)
            ? jobApplications[0]
            : jobApplications.FirstOrDefault(path => string.Equals(path, selectedJobApplicationPath, StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(jobApplication))
        {
            throw new FileNotFoundException(
                $"The selected sample job application file was not found in: {jobDirectory}",
                selectedJobApplicationPath ?? jobDirectory);
        }

        return new SampleDataContext(personDirectory, personFiles, jobApplication, preferencesFilePath);
    }

    private string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "..", ".."));
    }

    private string GetParsingSchemaPath(string schemaFileName)
    {
        return Path.Combine(GetRepositoryRoot(), "LLM", "AI Schemas", "LLM Parsing", schemaFileName);
    }

    /// <summary>
    /// Allocates the next monotonically increasing run directory under LLM/Results.
    /// </summary>
    private string CreateNextRunDirectory()
    {
        var resultsRoot = Path.Combine(GetRepositoryRoot(), "LLM", "Results");
        Directory.CreateDirectory(resultsRoot);

        lock (ResultsDirectoryLock)
        {
            var nextRunNumber = Directory.GetDirectories(resultsRoot, "Run *", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Select(TryParseRunNumber)
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .DefaultIfEmpty(0)
                .Max() + 1;

            while (true)
            {
                var runDirectory = Path.Combine(resultsRoot, $"Run {nextRunNumber}");
                if (!Directory.Exists(runDirectory))
                {
                    Directory.CreateDirectory(runDirectory);
                    return runDirectory;
                }

                nextRunNumber++;
            }
        }
    }

    private static string BuildDocumentId(string prefix, string source)
    {
        var normalized = new string(source
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray())
            .Trim('_');

        while (normalized.Contains("__", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("__", "_", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? prefix : $"{prefix}_{normalized}";
    }

    private static async Task<string> ReadRequiredTextAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Required asset file was not found: {filePath}", filePath);
        }

        var content = await File.ReadAllTextAsync(filePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Required asset file was empty: {filePath}");
        }

        return content;
    }

    private async Task SaveJsonResultAsync(string runDirectory, string fileName, string json, CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(runDirectory, fileName);
        var destinationRelativePath = Path.GetRelativePath(GetRepositoryRoot(), destinationPath);
        var formattedJson = FormatJson(json);

        await File.WriteAllTextAsync(destinationPath, formattedJson + Environment.NewLine, cancellationToken);

        _logger.LogInformation("Saved pipeline result to {ResultFile}.", destinationRelativePath);
    }

    // Matching repair starts with deterministic downgrades and only regenerates if conservative fixes are not enough.
    private async Task<MatchingStageRecoveryOutcome> RepairAndMaybeRegenerateMatchingAsync(
        string runDirectory,
        string matchingJson,
        string matchingDocumentId,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        StageVerificationResult currentStageResult,
        CancellationToken cancellationToken)
    {
        var currentJson = matchingJson;
        var currentResult = currentStageResult;

        for (var attempt = 1; attempt <= Math.Max(0, _verificationOptions.MaxRepairAttemptsPerStage) && ShouldAttemptMatchingRecovery(currentResult); attempt++)
        {
            _logger.LogInformation("Matching deterministic repair attempt {Attempt}: started.", attempt);

            var repairResult = await _matchingDeterministicRepairService.RepairAsync(currentJson, cancellationToken);
            if (!repairResult.WasModified)
            {
                _logger.LogInformation("Matching deterministic repair attempt {Attempt}: no conservative changes were available.", attempt);
                break;
            }

            await SaveTextArtifactAsync(runDirectory, "repair", $"matching_repair_attempt_{attempt}_input.json", currentJson, formatAsJson: true, cancellationToken);
            await SaveTextArtifactAsync(runDirectory, "repair", $"matching_repair_attempt_{attempt}_output.json", repairResult.RepairedJson, formatAsJson: true, cancellationToken);
            await SaveArtifactAsync(runDirectory, "repair", $"matching_repair_attempt_{attempt}_summary.json", repairResult, cancellationToken);

            currentJson = repairResult.RepairedJson;
            currentResult = await VerifyAndPersistStageAsync(
                request: CreateMatchingVerificationRequest(
                    matchingJson: currentJson,
                    matchingDocumentId: matchingDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId),
                runDirectory: runDirectory,
                artifactFileName: $"matching_repair_attempt_{attempt}_verification.json",
                gateArtifactFileName: $"matching_repair_attempt_{attempt}_gate.json",
                cancellationToken: cancellationToken,
                artifactSubDirectoryName: "repair");

            _logger.LogInformation(
                "Matching deterministic repair attempt {Attempt}: completed with gate decision {Decision}.",
                attempt,
                currentResult.Gate.Decision);
        }

        for (var attempt = 1; attempt <= Math.Max(0, _verificationOptions.MaxRegenerationAttemptsPerStage) && !currentResult.ApprovedForDownstream; attempt++)
        {
            _logger.LogInformation("Matching regeneration attempt {Attempt}: started.", attempt);

            var feedback = BuildMatchingRegenerationFeedback(currentResult);
            var feedbackJson = JsonSerializer.Serialize(feedback, SavedJsonOptions);
            await SaveTextArtifactAsync(runDirectory, "repair", $"matching_regeneration_attempt_{attempt}_feedback.json", feedbackJson, formatAsJson: true, cancellationToken);

            currentJson = await RunMatchingCoreAsync(
                requirementsJson,
                requirementsDocumentId,
                candidateEvidenceJson,
                candidateEvidenceDocumentId,
                regenerationFeedbackJson: feedbackJson,
                cancellationToken);

            await SaveTextArtifactAsync(runDirectory, "repair", $"matching_regeneration_attempt_{attempt}_output.json", currentJson, formatAsJson: true, cancellationToken);

            currentResult = await VerifyAndPersistStageAsync(
                request: CreateMatchingVerificationRequest(
                    matchingJson: currentJson,
                    matchingDocumentId: matchingDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId),
                runDirectory: runDirectory,
                artifactFileName: $"matching_regeneration_attempt_{attempt}_verification.json",
                gateArtifactFileName: $"matching_regeneration_attempt_{attempt}_gate.json",
                cancellationToken: cancellationToken,
                artifactSubDirectoryName: "repair");

            _logger.LogInformation(
                "Matching regeneration attempt {Attempt}: completed with gate decision {Decision}.",
                attempt,
                currentResult.Gate.Decision);
        }

        return new MatchingStageRecoveryOutcome(currentJson, currentResult);
    }

    private StageVerificationRequest CreateMatchingVerificationRequest(
        string matchingJson,
        string matchingDocumentId,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId)
    {
        return new StageVerificationRequest
        {
            Stage = VerificationStage.Matching,
            DocumentId = matchingDocumentId,
            DocumentJson = matchingJson,
            OutputSchemaPath = GetParsingSchemaPath("matching_schema.json"),
            RequirementsDocumentJson = requirementsJson,
            CandidateEvidenceDocumentJson = candidateEvidenceJson,
            ExpectedRequirementsDocumentId = requirementsDocumentId,
            ExpectedCandidateEvidenceDocumentId = candidateEvidenceDocumentId
        };
    }

    private StageVerificationRequest CreateRequirementsVerificationRequest(
        string requirementsJson,
        string requirementsDocumentId,
        string jobApplicationPath)
    {
        return new StageVerificationRequest
        {
            Stage = VerificationStage.Requirements,
            DocumentId = requirementsDocumentId,
            DocumentJson = requirementsJson,
            OutputSchemaPath = GetParsingSchemaPath("requirements_schema.json"),
            ExpectedParsedFiles = [Path.GetFileName(jobApplicationPath)],
            AllowedCitationFiles = [Path.GetFileName(jobApplicationPath)]
        };
    }

    private StageVerificationRequest CreateApplicationGenerationVerificationRequest(
        string applicationJson,
        string applicationDocumentId,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string matchingJson,
        string matchingDocumentId)
    {
        return new StageVerificationRequest
        {
            Stage = VerificationStage.ApplicationGeneration,
            DocumentId = applicationDocumentId,
            DocumentJson = applicationJson,
            OutputSchemaPath = GetParsingSchemaPath("application_generation_schema.json"),
            RequirementsDocumentJson = requirementsJson,
            CandidateEvidenceDocumentJson = candidateEvidenceJson,
            MatchingDocumentJson = matchingJson,
            ExpectedRequirementsDocumentId = requirementsDocumentId,
            ExpectedCandidateEvidenceDocumentId = candidateEvidenceDocumentId,
            ExpectedMatchingDocumentId = matchingDocumentId,
            ExpectedApplicationDocumentId = applicationDocumentId
        };
    }

    private static MatchingRegenerationFeedback BuildMatchingRegenerationFeedback(StageVerificationResult stageResult)
    {
        return new MatchingRegenerationFeedback
        {
            Decision = stageResult.Gate.Decision,
            BlockingReasons = stageResult.Gate.BlockingReasons,
            FailedMetrics = stageResult.Gate.Metrics.Where(metric => !metric.Passed).ToList(),
            Findings = stageResult.Findings,
            GuidanceDa = "Regenerer matching mere konservativt. Et krav uden konkrete matched_evidence_ids må ikke have confidence 'high'. Hvis ingen evidens reelt støtter kravet, brug not_matched eller unclear og lad matched_evidence_ids være tom. Opfind ikke nye evidence_id'er eller nye krav."
        };
    }

    private static bool ShouldAttemptMatchingRecovery(StageVerificationResult stageResult)
    {
        if (!stageResult.ApprovedForDownstream)
        {
            return true;
        }

        return stageResult.Findings.Any(finding =>
            string.Equals(finding.RuleId, "matching.high_confidence_without_evidence", StringComparison.Ordinal)
            || string.Equals(finding.RuleId, "matching.matched_without_evidence", StringComparison.Ordinal));
    }

    private async Task<RequirementsStageRecoveryOutcome> RepairRequirementsAsync(
        string runDirectory,
        string requirementsJson,
        string requirementsDocumentId,
        string jobApplicationPath,
        StageVerificationResult currentStageResult,
        CancellationToken cancellationToken)
    {
        var currentJson = requirementsJson;
        var currentResult = currentStageResult;

        for (var attempt = 1; attempt <= Math.Max(0, _verificationOptions.MaxRepairAttemptsPerStage) && ShouldAttemptRequirementsRecovery(currentResult); attempt++)
        {
            _logger.LogInformation("Requirements deterministic repair attempt {Attempt}: started.", attempt);

            var repairResult = await _requirementsDeterministicRepairService.RepairAsync(currentJson, cancellationToken);
            if (!repairResult.WasModified)
            {
                _logger.LogInformation("Requirements deterministic repair attempt {Attempt}: no conservative changes were available.", attempt);
                break;
            }

            await SaveTextArtifactAsync(runDirectory, "repair", $"requirements_repair_attempt_{attempt}_input.json", currentJson, formatAsJson: true, cancellationToken);
            await SaveTextArtifactAsync(runDirectory, "repair", $"requirements_repair_attempt_{attempt}_output.json", repairResult.RepairedJson, formatAsJson: true, cancellationToken);
            await SaveArtifactAsync(runDirectory, "repair", $"requirements_repair_attempt_{attempt}_summary.json", repairResult, cancellationToken);

            currentJson = repairResult.RepairedJson;
            currentResult = await VerifyAndPersistStageAsync(
                request: CreateRequirementsVerificationRequest(currentJson, requirementsDocumentId, jobApplicationPath),
                runDirectory: runDirectory,
                artifactFileName: $"requirements_repair_attempt_{attempt}_verification.json",
                gateArtifactFileName: $"requirements_repair_attempt_{attempt}_gate.json",
                cancellationToken: cancellationToken,
                artifactSubDirectoryName: "repair");

            _logger.LogInformation(
                "Requirements deterministic repair attempt {Attempt}: completed with gate decision {Decision}.",
                attempt,
                currentResult.Gate.Decision);
        }

        return new RequirementsStageRecoveryOutcome(currentJson, currentResult);
    }

    private static bool ShouldAttemptRequirementsRecovery(StageVerificationResult stageResult)
    {
        return !stageResult.ApprovedForDownstream;
    }

    // Application repair is intentionally local: fix references first rather than asking the model for a new document.
    private async Task<ApplicationGenerationStageRecoveryOutcome> RepairApplicationGenerationAsync(
        string runDirectory,
        string applicationJson,
        string applicationDocumentId,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string matchingJson,
        string matchingDocumentId,
        StageVerificationResult currentStageResult,
        CancellationToken cancellationToken)
    {
        var currentJson = applicationJson;
        var currentResult = currentStageResult;

        for (var attempt = 1; attempt <= Math.Max(0, _verificationOptions.MaxRepairAttemptsPerStage) && ShouldAttemptApplicationGenerationRecovery(currentResult); attempt++)
        {
            _logger.LogInformation("Application generation deterministic repair attempt {Attempt}: started.", attempt);

            var repairResult = await _applicationGenerationDeterministicRepairService.RepairAsync(
                currentJson,
                requirementsJson,
                candidateEvidenceJson,
                cancellationToken);

            if (!repairResult.WasModified)
            {
                _logger.LogInformation("Application generation deterministic repair attempt {Attempt}: no conservative changes were available.", attempt);
                break;
            }

            await SaveTextArtifactAsync(runDirectory, "repair", $"application_generation_repair_attempt_{attempt}_input.json", currentJson, formatAsJson: true, cancellationToken);
            await SaveTextArtifactAsync(runDirectory, "repair", $"application_generation_repair_attempt_{attempt}_output.json", repairResult.RepairedJson, formatAsJson: true, cancellationToken);
            await SaveArtifactAsync(runDirectory, "repair", $"application_generation_repair_attempt_{attempt}_summary.json", repairResult, cancellationToken);

            currentJson = repairResult.RepairedJson;
            currentResult = await VerifyAndPersistStageAsync(
                request: CreateApplicationGenerationVerificationRequest(
                    applicationJson: currentJson,
                    applicationDocumentId: applicationDocumentId,
                    requirementsJson: requirementsJson,
                    requirementsDocumentId: requirementsDocumentId,
                    candidateEvidenceJson: candidateEvidenceJson,
                    candidateEvidenceDocumentId: candidateEvidenceDocumentId,
                    matchingJson: matchingJson,
                    matchingDocumentId: matchingDocumentId),
                runDirectory: runDirectory,
                artifactFileName: $"application_generation_repair_attempt_{attempt}_verification.json",
                gateArtifactFileName: $"application_generation_repair_attempt_{attempt}_gate.json",
                cancellationToken: cancellationToken,
                artifactSubDirectoryName: "repair");

            _logger.LogInformation(
                "Application generation deterministic repair attempt {Attempt}: completed with gate decision {Decision}.",
                attempt,
                currentResult.Gate.Decision);
        }

        return new ApplicationGenerationStageRecoveryOutcome(currentJson, currentResult);
    }

    private static bool ShouldAttemptApplicationGenerationRecovery(StageVerificationResult stageResult)
    {
        return !stageResult.ApprovedForDownstream;
    }

    private async Task<StageVerificationResult> VerifyAndPersistStageAsync(
        StageVerificationRequest request,
        string runDirectory,
        string artifactFileName,
        string gateArtifactFileName,
        CancellationToken cancellationToken,
        string artifactSubDirectoryName = "verification")
    {
        _logger.LogInformation("Verification for stage {Stage}: started.", ToStageName(request.Stage));

        var result = await _verificationOrchestrator.VerifyStageAsync(request, cancellationToken);
        var artifactPath = await SaveArtifactAsync(runDirectory, artifactSubDirectoryName, artifactFileName, result, cancellationToken);
        var gateResult = _downstreamGateEvaluator.Evaluate(request, result);
        var gateArtifactPath = await SaveArtifactAsync(runDirectory, artifactSubDirectoryName, gateArtifactFileName, gateResult, cancellationToken);

        _logger.LogInformation(
            "Verification for stage {Stage}: completed with status {Status}.",
            result.Stage,
            result.Status);
        _logger.LogInformation(
            "Gate evaluation for stage {Stage}: completed with decision {Decision}.",
            result.Stage,
            gateResult.Decision);

        return new StageVerificationResult
        {
            Stage = result.Stage,
            DocumentId = result.DocumentId,
            VerificationMode = "mechanical_with_gate",
            Status = result.Status,
            ApprovedForDownstream = result.ApprovedForDownstream && gateResult.ApprovedForDownstream,
            WarningCount = result.WarningCount,
            ErrorCount = result.ErrorCount,
            ArtifactPath = artifactPath,
            GateArtifactPath = gateArtifactPath,
            Gate = gateResult,
            Findings = result.Findings
        };
    }

    private async Task<string> SaveVerificationArtifactAsync<TArtifact>(string runDirectory, string fileName, TArtifact artifact, CancellationToken cancellationToken)
    {
        return await SaveArtifactAsync(runDirectory, "verification", fileName, artifact, cancellationToken);
    }

    private async Task<string> SaveArtifactAsync<TArtifact>(
        string runDirectory,
        string subDirectoryName,
        string fileName,
        TArtifact artifact,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.Combine(runDirectory, subDirectoryName);
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, fileName);
        var destinationRelativePath = Path.GetRelativePath(GetRepositoryRoot(), destinationPath);
        var json = JsonSerializer.Serialize(artifact, SavedJsonOptions);

        await File.WriteAllTextAsync(destinationPath, json + Environment.NewLine, cancellationToken);

        _logger.LogInformation("Saved artifact to {ResultFile}.", destinationRelativePath);
        return destinationRelativePath;
    }

    private async Task<string> SaveTextArtifactAsync(
        string runDirectory,
        string subDirectoryName,
        string fileName,
        string content,
        bool formatAsJson,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.Combine(runDirectory, subDirectoryName);
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, fileName);
        var destinationRelativePath = Path.GetRelativePath(GetRepositoryRoot(), destinationPath);
        var outputContent = formatAsJson ? FormatJson(content) : content;

        await File.WriteAllTextAsync(destinationPath, outputContent + Environment.NewLine, cancellationToken);

        _logger.LogInformation("Saved artifact to {ResultFile}.", destinationRelativePath);
        return destinationRelativePath;
    }

    /// <summary>
    /// Aggregates stage-level verification and gate decisions into the top-level pipeline status.
    /// </summary>
    private static PipelineVerificationSummary BuildPipelineVerificationSummary(IReadOnlyList<StageVerificationResult> stageResults, bool completedAllStages)
    {
        var errorCount = stageResults.Sum(result => result.ErrorCount);
        var warningCount = stageResults.Sum(result => result.WarningCount);
        var hardInvalidCount = stageResults.Sum(result => result.Gate.HardInvalidCount);
        var softQualityCount = stageResults.Sum(result => result.Gate.SoftQualityCount);
        var stoppedAfterStage = stageResults.FirstOrDefault(result => !result.ApprovedForDownstream)?.Stage;
        var hasAdvisoryContinuation = stageResults.Any(result => string.Equals(result.Gate.Decision, "continue_with_advisory", StringComparison.Ordinal));

        return new PipelineVerificationSummary
        {
            VerificationMode = "mechanical_with_gate",
            PipelineStatus = stoppedAfterStage is null
                ? (completedAllStages ? "completed" : "incomplete")
                : "blocked",
            StoppedAfterStage = stoppedAfterStage,
            RecommendedAction = stoppedAfterStage is null
                ? (hasAdvisoryContinuation ? "continue_with_advisory" : "continue")
                : "repair_or_regenerate",
            Status = errorCount > 0 ? "fail" : warningCount > 0 ? "pass_with_warnings" : "pass",
            ApprovedForDownstream = stoppedAfterStage is null && errorCount == 0 && stageResults.All(result => result.ApprovedForDownstream),
            WarningCount = warningCount,
            ErrorCount = errorCount,
            HardInvalidCount = hardInvalidCount,
            SoftQualityCount = softQualityCount,
            Stages = stageResults.ToList()
        };
    }

    private static JsonElement ParseJsonElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string ToStageName(VerificationStage stage)
    {
        return stage switch
        {
            VerificationStage.Requirements => "requirements",
            VerificationStage.CandidateEvidence => "candidate_evidence",
            VerificationStage.Matching => "matching",
            VerificationStage.ApplicationGeneration => "application_generation",
            _ => stage.ToString().ToLowerInvariant()
        };
    }

    private static string FormatJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, SavedJsonOptions);
    }

    private static JobListingPipelineRunSummary BuildJobListingPipelineRunSummary(string jobListingFileName, PipelineExecutionResult executionResult)
    {
        var (overallMatchLevel, matchingSummaryDa, matchedCount, partiallyMatchedCount, notMatchedCount, unclearCount) =
            ParseMatchingSummary(executionResult.MatchingJson);
        var (selectedRequirementCount, omittedRequirementCount, applicationCoreMessageDa) =
            ParseApplicationSummary(executionResult.ApplicationJson);

        return new JobListingPipelineRunSummary
        {
            JobListingFileName = jobListingFileName,
            RunDirectory = executionResult.RunDirectoryRelativePath,
            PipelineStatus = executionResult.VerificationSummary?.PipelineStatus ?? "unknown",
            StoppedAfterStage = executionResult.VerificationSummary?.StoppedAfterStage,
            RecommendedAction = executionResult.VerificationSummary?.RecommendedAction ?? "inspect",
            ApplicationGenerated = executionResult.ApplicationJson is not null,
            OverallMatchLevel = overallMatchLevel,
            MatchingSummaryDa = matchingSummaryDa,
            ApplicationCoreMessageDa = applicationCoreMessageDa,
            FitGuidanceMode = executionResult.FitAdvisory?.GuidanceMode,
            FitAdvisoryDa = executionResult.FitAdvisory?.SummaryDa,
            FitAdvisoryArtifactPath = executionResult.FitAdvisory?.ArtifactPath,
            MatchedCount = matchedCount,
            PartiallyMatchedCount = partiallyMatchedCount,
            NotMatchedCount = notMatchedCount,
            UnclearCount = unclearCount,
            SelectedRequirementCount = selectedRequirementCount,
            OmittedRequirementCount = omittedRequirementCount
        };
    }

    private async Task<FitStrategyPreferences> LoadFitStrategyPreferencesAsync(string? preferencesFilePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(preferencesFilePath) || !File.Exists(preferencesFilePath))
        {
            return new FitStrategyPreferences("optimistic", IncludeFitAdvisory: false, AllowApplicationOnWeakMatch: true, PreferTransferableStrengthsWhenDirectMatchIsWeak: true, AllowStretchPositioning: true);
        }

        var preferencesJson = await File.ReadAllTextAsync(preferencesFilePath, cancellationToken);
        using var document = JsonDocument.Parse(preferencesJson);
        if (!document.RootElement.TryGetProperty("fit_strategy", out var fitStrategy) || fitStrategy.ValueKind != JsonValueKind.Object)
        {
            return new FitStrategyPreferences("optimistic", IncludeFitAdvisory: false, AllowApplicationOnWeakMatch: true, PreferTransferableStrengthsWhenDirectMatchIsWeak: true, AllowStretchPositioning: true);
        }

        var guidanceMode = NormalizeGuidanceMode(GetString(fitStrategy, "guidance_mode"));
        var includeFitAdvisory = GetBoolean(fitStrategy, "include_fit_advisory", defaultValue: false);
        var allowApplicationOnWeakMatch = GetBoolean(fitStrategy, "allow_application_on_weak_match", defaultValue: true);
        var preferTransferableStrengthsWhenDirectMatchIsWeak = GetBoolean(fitStrategy, "prefer_transferable_strengths_when_direct_match_is_weak", defaultValue: true);
        var allowStretchPositioning = GetBoolean(fitStrategy, "allow_stretch_positioning", defaultValue: true);

        return new FitStrategyPreferences(
            guidanceMode,
            includeFitAdvisory,
            allowApplicationOnWeakMatch,
            preferTransferableStrengthsWhenDirectMatchIsWeak,
            allowStretchPositioning);
    }

    /// <summary>
    /// Creates and persists the optional advisory text that explains how fit was framed for the user.
    /// </summary>
    private async Task<FitAdvisorySummary?> BuildAndMaybeSaveFitAdvisoryAsync(
        string runDirectory,
        FitStrategyPreferences fitStrategy,
        string? matchingJson,
        string? applicationJson,
        PipelineVerificationSummary? verificationSummary,
        CancellationToken cancellationToken)
    {
        if (!fitStrategy.IncludeFitAdvisory)
        {
            return null;
        }

        var (overallMatchLevel, _, matchedCount, partiallyMatchedCount, notMatchedCount, unclearCount) = ParseMatchingSummary(matchingJson);
        var (_, _, applicationCoreMessageDa) = ParseApplicationSummary(applicationJson);

        var summaryDa = BuildFitAdvisoryText(
            fitStrategy,
            verificationSummary,
            overallMatchLevel,
            matchedCount,
            partiallyMatchedCount,
            notMatchedCount,
            unclearCount,
            applicationGenerated: applicationJson is not null,
            applicationCoreMessageDa);

        var advisory = new FitAdvisorySummary
        {
            GuidanceMode = fitStrategy.GuidanceMode,
            SummaryDa = summaryDa
        };

        var artifactPath = await SaveArtifactAsync(runDirectory, "advisory", "fit_advisory.json", advisory, cancellationToken);
        return new FitAdvisorySummary
        {
            GuidanceMode = advisory.GuidanceMode,
            SummaryDa = advisory.SummaryDa,
            ArtifactPath = artifactPath
        };
    }

    /// <summary>
    /// Produces human-readable fit guidance without changing whether the run is allowed to continue.
    /// </summary>
    private static string BuildFitAdvisoryText(
        FitStrategyPreferences fitStrategy,
        PipelineVerificationSummary? verificationSummary,
        string? overallMatchLevel,
        int matchedCount,
        int partiallyMatchedCount,
        int notMatchedCount,
        int unclearCount,
        bool applicationGenerated,
        string? applicationCoreMessageDa)
    {
        if (!string.IsNullOrWhiteSpace(verificationSummary?.StoppedAfterStage))
        {
            return $"Kørslen blev stoppet ved {verificationSummary.StoppedAfterStage} på grund af verifikations- eller integritetsregler, ikke fordi systemet vurderede, at brugeren ikke bør søge stillingen. Brugeren bestemmer stadig selv, om jobbet skal forfølges.";
        }

        var matchDescriptor = string.IsNullOrWhiteSpace(overallMatchLevel) ? "ukendt" : overallMatchLevel;
        var verdictSummary = $"Matchniveauet ser {matchDescriptor} ud med {matchedCount} matched, {partiallyMatchedCount} delvist matched, {notMatchedCount} not_matched og {unclearCount} unclear krav.";

        return fitStrategy.GuidanceMode switch
        {
            "protective" => $"Denne advisory er sat til protective. {verdictSummary} Systemet har stadig forsøgt at skrive den mest sandfærdige ansøgning muligt uden at diktere, om brugeren bør søge. Fokus bør lægges på dokumenterede styrker og overførbare kompetencer.",
            "balanced" => $"Denne advisory er sat til balanced. {verdictSummary} Systemet har skrevet en ansøgning, hvis det kunne gøres sandfærdigt, men holder en mere nøgtern vurdering af rollefit. {(applicationGenerated && !string.IsNullOrWhiteSpace(applicationCoreMessageDa) ? "Ansøgningen fokuserer på de bedst understøttede styrker." : "Der blev ikke fastholdt en færdig ansøgning i denne kørsel.")}",
            _ => $"Denne advisory er sat til optimistic. {verdictSummary} Systemet har forsøgt at skrive den stærkest mulige sandfærdige ansøgning og fremhæve overførbare styrker frem for at agere gatekeeper. {(applicationGenerated && !string.IsNullOrWhiteSpace(applicationCoreMessageDa) ? "Ansøgningen fokuserer på de bedst dokumenterede og mest overførbare styrker." : "Denne kørsel fastholdt ikke en færdig ansøgning, men det skyldes verifikation og ikke et automatisk afslag på at søge.")}"
        };
    }

    private static (string? OverallMatchLevel, string? MatchingSummaryDa, int MatchedCount, int PartiallyMatchedCount, int NotMatchedCount, int UnclearCount) ParseMatchingSummary(string? matchingJson)
    {
        if (string.IsNullOrWhiteSpace(matchingJson))
        {
            return (null, null, 0, 0, 0, 0);
        }

        using var document = JsonDocument.Parse(matchingJson);
        var matchedCount = 0;
        var partiallyMatchedCount = 0;
        var notMatchedCount = 0;
        var unclearCount = 0;

        if (document.RootElement.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
        {
            foreach (var match in matches.EnumerateArray())
            {
                var verdict = GetString(match, "verdict");
                switch (verdict)
                {
                    case "matched":
                        matchedCount++;
                        break;
                    case "partially_matched":
                        partiallyMatchedCount++;
                        break;
                    case "not_matched":
                        notMatchedCount++;
                        break;
                    case "unclear":
                        unclearCount++;
                        break;
                }
            }
        }

        var overallMatchLevel = document.RootElement.TryGetProperty("overall_assessment", out var overallAssessment)
            ? GetString(overallAssessment, "overall_match_level")
            : null;
        var matchingSummaryDa = document.RootElement.TryGetProperty("overall_assessment", out overallAssessment)
            ? GetString(overallAssessment, "summary_da")
            : null;

        return (overallMatchLevel, matchingSummaryDa, matchedCount, partiallyMatchedCount, notMatchedCount, unclearCount);
    }

    private static (int SelectedRequirementCount, int OmittedRequirementCount, string? ApplicationCoreMessageDa) ParseApplicationSummary(string? applicationJson)
    {
        if (string.IsNullOrWhiteSpace(applicationJson))
        {
            return (0, 0, null);
        }

        using var document = JsonDocument.Parse(applicationJson);
        if (!document.RootElement.TryGetProperty("application_strategy", out var strategy) || strategy.ValueKind != JsonValueKind.Object)
        {
            return (0, 0, null);
        }

        var selectedRequirementCount = strategy.TryGetProperty("selected_requirement_ids", out var selectedRequirementIds)
            && selectedRequirementIds.ValueKind == JsonValueKind.Array
                ? selectedRequirementIds.GetArrayLength()
                : 0;
        var omittedRequirementCount = strategy.TryGetProperty("omitted_requirement_ids", out var omittedRequirementIds)
            && omittedRequirementIds.ValueKind == JsonValueKind.Array
                ? omittedRequirementIds.GetArrayLength()
                : 0;
        var applicationCoreMessageDa = GetString(strategy, "core_message_da");

        return (selectedRequirementCount, omittedRequirementCount, applicationCoreMessageDa);
    }

    private static string GetString(JsonElement parent, string propertyName)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool GetBoolean(JsonElement parent, string propertyName, bool defaultValue)
    {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : defaultValue;
    }

    private static string NormalizeGuidanceMode(string? guidanceMode)
    {
        return guidanceMode?.Trim().ToLowerInvariant() switch
        {
            "balanced" => "balanced",
            "protective" => "protective",
            _ => "optimistic"
        };
    }

    private static int? TryParseRunNumber(string? directoryName)
    {
        const string prefix = "Run ";

        if (string.IsNullOrWhiteSpace(directoryName) || !directoryName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return int.TryParse(directoryName[prefix.Length..], out var number) && number > 0
            ? number
            : null;
    }

    private sealed record FlowAsset(string Prompt, string SchemaName, string? SchemaDescription, JsonElement OutputSchema);

    private sealed record SampleDataContext(
        string CandidateDirectoryPath,
        IReadOnlyList<string> PersonFiles,
        string JobApplication,
        string? PreferencesFilePath)
    {
        public string CandidateDirectoryName => Path.GetFileName(CandidateDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private sealed record PipelineExecutionResult(
        string JobListingFileName,
        string RunDirectory,
        string RunDirectoryRelativePath,
        string? MatchingJson,
        string? ApplicationJson,
        PipelineVerificationSummary? VerificationSummary,
        FitAdvisorySummary? FitAdvisory);

    private sealed record RequirementsStageRecoveryOutcome(string RequirementsJson, StageVerificationResult StageResult);

    private sealed record MatchingStageRecoveryOutcome(string MatchingJson, StageVerificationResult StageResult);

    private sealed record ApplicationGenerationStageRecoveryOutcome(string ApplicationJson, StageVerificationResult StageResult);

    private sealed record FitStrategyPreferences(
        string GuidanceMode,
        bool IncludeFitAdvisory,
        bool AllowApplicationOnWeakMatch,
        bool PreferTransferableStrengthsWhenDirectMatchIsWeak,
        bool AllowStretchPositioning);
}