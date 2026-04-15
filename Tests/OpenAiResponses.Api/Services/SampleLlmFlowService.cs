using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenAiResponses.Api.Helpers;
using OpenAiResponses.Api.Models;
using OpenAiResponses.Api.Options;

namespace OpenAiResponses.Api.Services;

/// <summary>
/// Orchestrates the sample pipeline, including company-context enrichment, verification, repair, advisory output, and artifact persistence.
/// </summary>
public sealed class SampleLlmFlowService : ISampleLlmFlowService
{
    private const string ResourceConsumptionDirectoryName = "Ressource Consumption";
    private const string TokenUsageFileName = "TokenUsage.json";
    private static readonly JsonSerializerOptions SavedJsonOptions = JsonSerializationDefaults.IndentedUtf8;
    private static readonly Lock ResultsDirectoryLock = new();

    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ICompanyContextService _companyContextService;
    private readonly IOpenAiResponsesService _openAiResponsesService;
    private readonly ICurrencyDisplayConversionService _currencyDisplayConversionService;
    private readonly ICoverLetterTemplateRenderer _coverLetterTemplateRenderer;
    private readonly ICoverLetterPdfRenderer _coverLetterPdfRenderer;
    private readonly IVerificationOrchestrator _verificationOrchestrator;
    private readonly IDownstreamGateEvaluator _downstreamGateEvaluator;
    private readonly IRequirementsDeterministicRepairService _requirementsDeterministicRepairService;
    private readonly IMatchingDeterministicRepairService _matchingDeterministicRepairService;
    private readonly IApplicationGenerationDeterministicRepairService _applicationGenerationDeterministicRepairService;
    private readonly ILogger<SampleLlmFlowService> _logger;
    private readonly OpenAIOptions _openAiOptions;
    private readonly SamplePipelineOptions _samplePipelineOptions;
    private readonly VerificationOptions _verificationOptions;

    public SampleLlmFlowService(
        IHostEnvironment environment,
        IConfiguration configuration,
        ICompanyContextService companyContextService,
        IOpenAiResponsesService openAiResponsesService,
        ICurrencyDisplayConversionService currencyDisplayConversionService,
        ICoverLetterTemplateRenderer coverLetterTemplateRenderer,
        ICoverLetterPdfRenderer coverLetterPdfRenderer,
        IVerificationOrchestrator verificationOrchestrator,
        IDownstreamGateEvaluator downstreamGateEvaluator,
        IOptions<OpenAIOptions> openAiOptions,
        IOptions<SamplePipelineOptions> samplePipelineOptions,
        IRequirementsDeterministicRepairService requirementsDeterministicRepairService,
        IMatchingDeterministicRepairService matchingDeterministicRepairService,
        IApplicationGenerationDeterministicRepairService applicationGenerationDeterministicRepairService,
        IOptions<VerificationOptions> verificationOptions,
        ILogger<SampleLlmFlowService> logger)
    {
        _environment = environment;
        _configuration = configuration;
        _companyContextService = companyContextService;
        _openAiResponsesService = openAiResponsesService;
        _currencyDisplayConversionService = currencyDisplayConversionService;
        _coverLetterTemplateRenderer = coverLetterTemplateRenderer;
        _coverLetterPdfRenderer = coverLetterPdfRenderer;
        _verificationOrchestrator = verificationOrchestrator;
        _downstreamGateEvaluator = downstreamGateEvaluator;
        _openAiOptions = openAiOptions.Value;
        _samplePipelineOptions = samplePipelineOptions.Value;
        _requirementsDeterministicRepairService = requirementsDeterministicRepairService;
        _matchingDeterministicRepairService = matchingDeterministicRepairService;
        _applicationGenerationDeterministicRepairService = applicationGenerationDeterministicRepairService;
        _verificationOptions = verificationOptions.Value;
        _logger = logger;
    }

    public async Task<string> RunCompanyContextAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var result = await RunCompanyContextCoreAsync(sampleData, cancellationToken);
        return result.OutputJson;
    }

    public async Task<string> RunRequirementsParsingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var result = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        return result.OutputJson;
    }

    public async Task<string> RunCandidateEvidenceAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsResult = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsJson = requirementsResult.OutputJson;
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));

        var candidateEvidenceResult = await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);
        return candidateEvidenceResult.OutputJson;
    }

    public async Task<string> RunMatchingAsync(CancellationToken cancellationToken = default)
    {
        var sampleData = GetSampleData();
        var requirementsResult = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        var requirementsJson = requirementsResult.OutputJson;
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceResult = await RunCandidateEvidenceCoreAsync(sampleData, requirementsJson, requirementsDocumentId, cancellationToken);
        var candidateEvidenceJson = candidateEvidenceResult.OutputJson;

        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        var matchingResult = await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            regenerationFeedbackJson: null,
            cancellationToken);

        return matchingResult.OutputJson;
    }

    public async Task<string> RunPipelineAsync(SamplePipelineSelectionRequest? selection = null, CancellationToken cancellationToken = default)
    {
        var executionResult = await ExecutePipelineAsync(includeVerification: false, cancellationToken: cancellationToken, selection: selection);
        return executionResult.ApplicationJson ?? throw new InvalidOperationException("Pipeline execution did not produce an application document.");
    }

    public async Task<string> RunPipelineWithVerificationAsync(SamplePipelineSelectionRequest? selection = null, CancellationToken cancellationToken = default)
    {
        var executionResult = await ExecutePipelineAsync(includeVerification: true, cancellationToken: cancellationToken, selection: selection);
        var response = new PipelineWithVerificationResponse
        {
            CandidateDirectory = executionResult.CandidateDirectoryName,
            JobListingFileName = executionResult.JobListingFileName,
            RunDirectory = executionResult.RunDirectoryRelativePath,
            PipelineStatus = executionResult.VerificationSummary?.PipelineStatus ?? "completed",
            StoppedAfterStage = executionResult.VerificationSummary?.StoppedAfterStage,
            ApplicationDocument = executionResult.ApplicationJson is null ? null : ParseJsonElement(executionResult.ApplicationJson),
            FitAdvisory = executionResult.FitAdvisory,
            CoverLetter = executionResult.CoverLetter,
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
                selection: null,
                selectedJobApplicationPath: jobApplication);

            jobSummaries.Add(BuildJobListingPipelineRunSummary(
                jobListingFileName: Path.GetFileName(jobApplication),
                executionResult: executionResult));
        }

        var response = new MultiJobPipelineWithVerificationResponse
        {
            CandidateDirectory = _samplePipelineOptions.DefaultCandidateDirectory,
            JobListingCount = jobSummaries.Count,
            Jobs = jobSummaries
        };

        return JsonSerializer.Serialize(response, SavedJsonOptions);
    }

    private async Task<PipelineExecutionResult> ExecutePipelineAsync(
        bool includeVerification,
        CancellationToken cancellationToken,
        SamplePipelineSelectionRequest? selection = null,
        string? selectedJobApplicationPath = null)
    {
        var sampleData = GetSampleData(selection, selectedJobApplicationPath);
        var fitStrategy = await LoadFitStrategyPreferencesAsync(sampleData.PreferencesFilePath, cancellationToken);
        var jobApplicationFileName = Path.GetFileName(sampleData.JobApplication);
        var requirementsDocumentId = BuildDocumentId("requirements", Path.GetFileNameWithoutExtension(sampleData.JobApplication));
        var candidateEvidenceDocumentId = BuildDocumentId("candidate_evidence", sampleData.CandidateDirectoryName);
        var companyContextDocumentId = BuildDocumentId(
            "company_context",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var matchingDocumentId = BuildDocumentId(
            "matching",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var applicationDocumentId = BuildDocumentId(
            "application",
            $"{sampleData.CandidateDirectoryName}_{Path.GetFileNameWithoutExtension(sampleData.JobApplication)}");
        var runDirectory = CreateNextRunDirectory();
        var runDirectoryRelativePath = Path.GetRelativePath(GetRepositoryRoot(), runDirectory);
        var stageVerificationResults = new List<StageVerificationResult>();
        var tokenUsageRecords = new List<LlmInteractionUsageRecord>();
        PipelineVerificationSummary? verificationSummary = null;
        string? applicationJson = null;
        var requirementsPhaseSettings = ResolvePhaseExecutionSettings("requirements");
        CoverLetterRenderArtifact? coverLetterArtifact = null;

        _logger.LogInformation("Pipeline run directory created at {RunDirectory}.", runDirectoryRelativePath);

        _logger.LogInformation(
            "Flow 1/5 requirements parsing: sending job application {JobApplicationFile} to OpenAI model {Model}.",
            Path.GetFileName(sampleData.JobApplication),
            requirementsPhaseSettings.Model);

        var requirementsResult = await RunRequirementsParsingCoreAsync(sampleData, cancellationToken);
        RecordLlmInteraction(tokenUsageRecords, phase: "requirements", sequenceKind: "initial_generation", attempt: null, requirementsResult, requirementsPhaseSettings.Pricing);
        var requirementsJson = requirementsResult.OutputJson;
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

                await SaveTokenUsageReportAsync(
                    runDirectory: runDirectory,
                    runDirectoryRelativePath: runDirectoryRelativePath,
                    jobListingFileName: jobApplicationFileName,
                    pipelineStatus: verificationSummary.PipelineStatus,
                    interactions: tokenUsageRecords,
                    cancellationToken: CancellationToken.None);

                return new PipelineExecutionResult(
                    CandidateDirectoryName: sampleData.CandidateDirectoryName,
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: null,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory,
                    CoverLetter: null);
            }
        }

        _logger.LogInformation("Flow 1/5 requirements parsing: OpenAI replied.");
        _logger.LogInformation("Flow 2/5 company context: started.");

        var companyContextResult = await RunCompanyContextCoreAsync(sampleData, cancellationToken);
        RecordLlmInteraction(
            tokenUsageRecords,
            phase: "company_context",
            sequenceKind: "initial_generation",
            attempt: null,
            companyContextResult,
            ResolvePhaseExecutionSettings("company_context").Pricing);
        var companyContextJson = companyContextResult.OutputJson;
        await SaveJsonResultAsync(runDirectory, "company_context.json", companyContextJson, cancellationToken);

        // CompanyContext is currently an enrichment stage without downstream gating.
        _logger.LogInformation("Flow 2/5 company context: completed.");
        _logger.LogInformation("Flow 3/5 candidate evidence: started.");

        var candidateEvidenceResult = await RunCandidateEvidenceCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            cancellationToken);
        RecordLlmInteraction(
            tokenUsageRecords,
            phase: "candidate_evidence",
            sequenceKind: "initial_generation",
            attempt: null,
            candidateEvidenceResult,
            ResolvePhaseExecutionSettings("candidate_evidence").Pricing);
        var candidateEvidenceJson = candidateEvidenceResult.OutputJson;
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

                await SaveTokenUsageReportAsync(
                    runDirectory: runDirectory,
                    runDirectoryRelativePath: runDirectoryRelativePath,
                    jobListingFileName: jobApplicationFileName,
                    pipelineStatus: verificationSummary.PipelineStatus,
                    interactions: tokenUsageRecords,
                    cancellationToken: CancellationToken.None);

                return new PipelineExecutionResult(
                    CandidateDirectoryName: sampleData.CandidateDirectoryName,
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: null,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory,
                    CoverLetter: null);
            }
        }

        _logger.LogInformation("Flow 3/5 candidate evidence: completed.");
        _logger.LogInformation("Flow 4/5 matching: started.");

        var matchingResult = await RunMatchingCoreAsync(
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            regenerationFeedbackJson: null,
            cancellationToken);
        RecordLlmInteraction(
            tokenUsageRecords,
            phase: "matching",
            sequenceKind: "initial_generation",
            attempt: null,
            matchingResult,
            ResolvePhaseExecutionSettings("matching").Pricing);
        var matchJson = matchingResult.OutputJson;
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
                    tokenUsageRecords: tokenUsageRecords,
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

                await SaveTokenUsageReportAsync(
                    runDirectory: runDirectory,
                    runDirectoryRelativePath: runDirectoryRelativePath,
                    jobListingFileName: jobApplicationFileName,
                    pipelineStatus: verificationSummary.PipelineStatus,
                    interactions: tokenUsageRecords,
                    cancellationToken: CancellationToken.None);

                return new PipelineExecutionResult(
                    CandidateDirectoryName: sampleData.CandidateDirectoryName,
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: matchJson,
                    ApplicationJson: null,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory,
                    CoverLetter: null);
            }
        }

        _logger.LogInformation("Flow 4/5 matching: completed.");
        _logger.LogInformation("Flow 5/5 application generation: started.");

        var applicationResult = await RunApplicationGenerationCoreAsync(
            sampleData,
            requirementsJson,
            requirementsDocumentId,
            candidateEvidenceJson,
            candidateEvidenceDocumentId,
            companyContextJson,
            companyContextDocumentId,
            matchJson,
            matchingDocumentId,
            applicationDocumentId,
            cancellationToken);
        RecordLlmInteraction(
            tokenUsageRecords,
            phase: "application_generation",
            sequenceKind: "initial_generation",
            attempt: null,
            applicationResult,
            ResolvePhaseExecutionSettings("application_generation").Pricing);
        applicationJson = applicationResult.OutputJson;
        await SaveJsonResultAsync(runDirectory, "application_generation.json", applicationJson, cancellationToken);

        var coverLetterArtifactsPersisted = false;

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
                    companyContextDocumentId: companyContextDocumentId,
                    matchingJson: matchJson,
                    matchingDocumentId: matchingDocumentId,
                    maxMainContentCharacters: _samplePipelineOptions.CoverLetterTemplate.MaxMainContentCharacters,
                    estimatedCharactersPerLine: _samplePipelineOptions.CoverLetterTemplate.EstimatedCharactersPerLine),
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
                    companyContextDocumentId: companyContextDocumentId,
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

            coverLetterArtifact = await SaveRenderedCoverLetterArtifactsAsync(runDirectory, applicationJson, cancellationToken);
            coverLetterArtifactsPersisted = true;

            if (!stageVerificationResults[^1].ApprovedForDownstream)
            {
                _logger.LogWarning("Pipeline completed all stages, but the downstream gate failed at {Stage}.", stageVerificationResults[^1].Stage);

                var fitAdvisory = includeVerification
                    ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchJson, applicationJson, verificationSummary, cancellationToken)
                    : null;

                await SaveTokenUsageReportAsync(
                    runDirectory: runDirectory,
                    runDirectoryRelativePath: runDirectoryRelativePath,
                    jobListingFileName: jobApplicationFileName,
                    pipelineStatus: verificationSummary.PipelineStatus,
                    interactions: tokenUsageRecords,
                    cancellationToken: CancellationToken.None);

                return new PipelineExecutionResult(
                    CandidateDirectoryName: sampleData.CandidateDirectoryName,
                    JobListingFileName: jobApplicationFileName,
                    RunDirectory: runDirectory,
                    RunDirectoryRelativePath: runDirectoryRelativePath,
                    MatchingJson: matchJson,
                    ApplicationJson: applicationJson,
                    VerificationSummary: verificationSummary,
                    FitAdvisory: fitAdvisory,
                    CoverLetter: coverLetterArtifact);
            }
        }

        _logger.LogInformation("Flow 5/5 application generation: completed.");
        _logger.LogInformation("Pipeline results saved to {RunDirectory}.", runDirectoryRelativePath);

        if (!coverLetterArtifactsPersisted)
        {
            coverLetterArtifact = await SaveRenderedCoverLetterArtifactsAsync(runDirectory, applicationJson, cancellationToken);
        }

        var completedFitAdvisory = includeVerification
            ? await BuildAndMaybeSaveFitAdvisoryAsync(runDirectory, fitStrategy, matchJson, applicationJson, verificationSummary, cancellationToken)
            : null;

        await SaveTokenUsageReportAsync(
            runDirectory: runDirectory,
            runDirectoryRelativePath: runDirectoryRelativePath,
            jobListingFileName: jobApplicationFileName,
            pipelineStatus: verificationSummary?.PipelineStatus ?? "completed",
            interactions: tokenUsageRecords,
            cancellationToken: CancellationToken.None);

        return new PipelineExecutionResult(
            CandidateDirectoryName: sampleData.CandidateDirectoryName,
            JobListingFileName: jobApplicationFileName,
            RunDirectory: runDirectory,
            RunDirectoryRelativePath: runDirectoryRelativePath,
            MatchingJson: matchJson,
            ApplicationJson: applicationJson,
            VerificationSummary: verificationSummary,
            FitAdvisory: completedFitAdvisory,
            CoverLetter: coverLetterArtifact);
    }

    private async Task<StructuredJsonGenerationResult> RunMatchingCoreAsync(
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
            Model = ResolvePhaseExecutionSettings("matching").Model,
            InputTexts = inputTexts
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(request, cancellationToken);
    }

    private async Task<StructuredJsonGenerationResult> RunCompanyContextCoreAsync(
        SampleDataContext sampleData,
        CancellationToken cancellationToken)
    {
        return await _companyContextService.GenerateCompanyContextAsync(
            new CompanyContextGenerationRequest
            {
                JobPostingFilePath = sampleData.JobApplication,
                ApplicantProfileFilePaths = ResolveCompanyContextApplicantProfileFiles(sampleData)
            },
            cancellationToken);
    }

    private async Task<StructuredJsonGenerationResult> RunApplicationGenerationCoreAsync(
        SampleDataContext sampleData,
        string requirementsJson,
        string requirementsDocumentId,
        string candidateEvidenceJson,
        string candidateEvidenceDocumentId,
        string companyContextJson,
        string companyContextDocumentId,
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
        preferencesJson = NormalizeApplicationGenerationPreferencesJson(preferencesJson);
        var request = new StructuredJsonResponseRequest
        {
            Prompt = applicationAsset.Prompt,
            SchemaName = applicationAsset.SchemaName,
            SchemaDescription = applicationAsset.SchemaDescription,
            OutputSchema = applicationAsset.OutputSchema,
            Model = ResolvePhaseExecutionSettings("application_generation").Model,
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
                    Label = "Company context document ID",
                    Content = companyContextDocumentId
                },
                new StructuredTextInput
                {
                    Label = "Company context document JSON",
                    Content = companyContextJson
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

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(request, cancellationToken);
    }

    private async Task<StructuredJsonGenerationResult> RunRequirementsParsingCoreAsync(SampleDataContext sampleData, CancellationToken cancellationToken)
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
            Model = ResolvePhaseExecutionSettings("requirements").Model,
            InputFiles =
            [
                new StructuredFileInput
                {
                    Label = "Job application file",
                    FilePath = sampleData.JobApplication
                }
            ]
        };

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(request, cancellationToken);
    }

    private async Task<StructuredJsonGenerationResult> RunCandidateEvidenceCoreAsync(
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
            Model = ResolvePhaseExecutionSettings("candidate_evidence").Model,
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

        return await _openAiResponsesService.GenerateStructuredJsonWithMetadataAsync(request, cancellationToken);
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
        var jobDirectory = ResolveRepositoryPath(_samplePipelineOptions.JobListingsPath);

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

    private IReadOnlyList<string> GetSampleCandidateDirectories(string candidateRootDirectory)
    {
        if (!Directory.Exists(candidateRootDirectory))
        {
            throw new FileNotFoundException($"Sample candidate root directory was not found: {candidateRootDirectory}", candidateRootDirectory);
        }

        var candidateDirectories = Directory.GetDirectories(candidateRootDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        if (candidateDirectories.Length == 0)
        {
            throw new FileNotFoundException($"No sample candidate directories were found in: {candidateRootDirectory}", candidateRootDirectory);
        }

        return candidateDirectories;
    }

    /// <summary>
    /// Resolves the selected sample candidate plus either the default or a caller-selected job posting.
    /// </summary>
    private SampleDataContext GetSampleData(SamplePipelineSelectionRequest? selection = null, string? selectedJobApplicationPath = null)
    {
        var candidateRootDirectory = ResolveRepositoryPath(_samplePipelineOptions.CandidateRootPath);
        var jobDirectory = ResolveRepositoryPath(_samplePipelineOptions.JobListingsPath);
        var personDirectory = ResolveSelectedCandidateDirectory(candidateRootDirectory, selection);

        if (!Directory.Exists(personDirectory))
        {
            throw new FileNotFoundException($"Sample candidate directory was not found: {personDirectory}", personDirectory);
        }

        if (!Directory.Exists(jobDirectory))
        {
            throw new FileNotFoundException($"Sample job directory was not found: {jobDirectory}", jobDirectory);
        }

        var personFiles = Directory.GetFiles(personDirectory)
            .Where(path => !string.Equals(Path.GetFileName(path), _samplePipelineOptions.PreferencesFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var preferencesFilePath = Directory.GetFiles(personDirectory)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), _samplePipelineOptions.PreferencesFileName, StringComparison.OrdinalIgnoreCase));

        if (personFiles.Length == 0)
        {
            throw new FileNotFoundException($"No candidate files were found in: {personDirectory}", personDirectory);
        }

        var jobApplications = GetSampleJobApplications();
        var jobApplication = ResolveSelectedJobApplication(jobApplications, jobDirectory, selection, selectedJobApplicationPath);

        if (string.IsNullOrWhiteSpace(jobApplication))
        {
            throw new FileNotFoundException(
                $"The selected sample job application file was not found in: {jobDirectory}",
                selectedJobApplicationPath ?? jobDirectory);
        }

        return new SampleDataContext(personDirectory, personFiles, jobApplication, preferencesFilePath);
    }

    private string ResolveSelectedCandidateDirectory(string candidateRootDirectory, SamplePipelineSelectionRequest? selection)
    {
        if (selection?.CandidateNumber is null)
        {
            return Path.Combine(candidateRootDirectory, _samplePipelineOptions.DefaultCandidateDirectory);
        }

        var candidateDirectories = GetSampleCandidateDirectories(candidateRootDirectory);
        var candidateNumber = selection.CandidateNumber.Value;

        if (candidateNumber < 1 || candidateNumber > candidateDirectories.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection.CandidateNumber),
                candidateNumber,
                $"candidateNumber must be between 1 and {candidateDirectories.Count}.");
        }

        return candidateDirectories[candidateNumber - 1];
    }

    private static string ResolveSelectedJobApplication(
        IReadOnlyList<string> jobApplications,
        string jobDirectory,
        SamplePipelineSelectionRequest? selection,
        string? selectedJobApplicationPath)
    {
        if (!string.IsNullOrWhiteSpace(selectedJobApplicationPath))
        {
            return jobApplications.FirstOrDefault(path => string.Equals(path, selectedJobApplicationPath, StringComparison.Ordinal))
                ?? throw new FileNotFoundException(
                    $"The selected sample job application file was not found in: {jobDirectory}",
                    selectedJobApplicationPath);
        }

        if (selection?.JobPostingNumber is null)
        {
            return jobApplications[0];
        }

        var jobPostingNumber = selection.JobPostingNumber.Value;
        if (jobPostingNumber < 1 || jobPostingNumber > jobApplications.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selection.JobPostingNumber),
                jobPostingNumber,
                $"jobPostingNumber must be between 1 and {jobApplications.Count}.");
        }

        return jobApplications[jobPostingNumber - 1];
    }

    private string GetRepositoryRoot()
    {
        return RepositoryRootResolver.GetRepositoryRoot(_configuration, _environment);
    }

    private string ResolveRepositoryPath(string configuredPath)
    {
        return Path.GetFullPath(Path.Combine(GetRepositoryRoot(), configuredPath));
    }

    private string GetParsingSchemaPath(string schemaFileName)
    {
        return Path.Combine(ResolveRepositoryPath(_samplePipelineOptions.ParsingSchemasPath), schemaFileName);
    }

    /// <summary>
    /// Allocates the next monotonically increasing run directory under the configured results root.
    /// </summary>
    private string CreateNextRunDirectory()
    {
        var resultsRoot = ResolveRepositoryPath(_samplePipelineOptions.ResultsPath);
        var runDirectoryPrefix = _samplePipelineOptions.RunDirectoryPrefix;
        Directory.CreateDirectory(resultsRoot);

        lock (ResultsDirectoryLock)
        {
            var nextRunNumber = Directory.GetDirectories(resultsRoot, $"{runDirectoryPrefix}*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Select(directoryName => TryParseRunNumber(directoryName, runDirectoryPrefix))
                .Where(number => number.HasValue)
                .Select(number => number!.Value)
                .DefaultIfEmpty(0)
                .Max() + 1;

            while (true)
            {
                var runDirectory = Path.Combine(resultsRoot, $"{runDirectoryPrefix}{nextRunNumber}");
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

        await File.WriteAllTextAsync(destinationPath, formattedJson + Environment.NewLine, JsonSerializationDefaults.Utf8NoBom, cancellationToken);

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
        ICollection<LlmInteractionUsageRecord> tokenUsageRecords,
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

            var regenerationResult = await RunMatchingCoreAsync(
                requirementsJson,
                requirementsDocumentId,
                candidateEvidenceJson,
                candidateEvidenceDocumentId,
                regenerationFeedbackJson: feedbackJson,
                cancellationToken);
            RecordLlmInteraction(
                tokenUsageRecords,
                phase: "matching",
                sequenceKind: "regeneration_after_gate_failure",
                attempt: attempt,
                regenerationResult,
                ResolvePhaseExecutionSettings("matching").Pricing);

            currentJson = regenerationResult.OutputJson;

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
        string companyContextDocumentId,
        string matchingJson,
        string matchingDocumentId,
        int maxMainContentCharacters,
        int estimatedCharactersPerLine)
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
            ExpectedCompanyContextDocumentId = companyContextDocumentId,
            ExpectedMatchingDocumentId = matchingDocumentId,
            ExpectedApplicationDocumentId = applicationDocumentId,
            MaxMainContentCharacters = maxMainContentCharacters,
            EstimatedCharactersPerLine = estimatedCharactersPerLine
        };
    }

    private string NormalizeApplicationGenerationPreferencesJson(string preferencesJson)
    {
        var templateMax = _samplePipelineOptions.CoverLetterTemplate.MaxMainContentCharacters;
        if (templateMax <= 0)
        {
            return preferencesJson;
        }

        JsonNode? parsedNode;
        try
        {
            parsedNode = JsonNode.Parse(preferencesJson);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Application generation preferences file did not contain valid JSON.", exception);
        }

        if (parsedNode is not JsonObject root)
        {
            throw new InvalidOperationException("Application generation preferences file must contain a JSON object at the root.");
        }

        var contentConstraints = root["content_constraints"] as JsonObject;
        if (contentConstraints is null)
        {
            contentConstraints = new JsonObject();
            root["content_constraints"] = contentConstraints;
        }

        int? configuredMax = null;
        if (contentConstraints["max_main_content_characters"] is JsonNode existingMaxNode)
        {
            configuredMax = existingMaxNode.GetValue<int>();
        }

        if (!configuredMax.HasValue || configuredMax.Value > templateMax)
        {
            contentConstraints["max_main_content_characters"] = templateMax;

            if (configuredMax.HasValue && configuredMax.Value != templateMax)
            {
                _logger.LogInformation(
                    "Application generation preferences requested max_main_content_characters={ConfiguredMax}, but template maximum is {TemplateMax}. The prompt input was clamped to the template limit.",
                    configuredMax.Value,
                    templateMax);
            }
        }

        return root.ToJsonString(JsonSerializationDefaults.IndentedUtf8);
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
        string companyContextDocumentId,
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
                    companyContextDocumentId: companyContextDocumentId,
                    matchingJson: matchingJson,
                    matchingDocumentId: matchingDocumentId,
                    maxMainContentCharacters: _samplePipelineOptions.CoverLetterTemplate.MaxMainContentCharacters,
                    estimatedCharactersPerLine: _samplePipelineOptions.CoverLetterTemplate.EstimatedCharactersPerLine),
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

    private async Task SaveTokenUsageReportAsync(
        string runDirectory,
        string runDirectoryRelativePath,
        string jobListingFileName,
        string pipelineStatus,
        IReadOnlyList<LlmInteractionUsageRecord> interactions,
        CancellationToken cancellationToken)
    {
        var pricing = BuildPricingConfigurationSnapshot();
        var report = await BuildTokenUsageReportAsync(
            runDirectoryRelativePath: runDirectoryRelativePath,
            jobListingFileName: jobListingFileName,
            pipelineStatus: pipelineStatus,
            pricing: pricing,
            interactions: interactions,
            cancellationToken: cancellationToken);

        await SaveArtifactAsync(runDirectory, ResourceConsumptionDirectoryName, TokenUsageFileName, report, cancellationToken);
    }

    private async Task<CoverLetterRenderArtifact> SaveRenderedCoverLetterArtifactsAsync(
        string runDirectory,
        string applicationJson,
        CancellationToken cancellationToken)
    {
        var templateOptions = _samplePipelineOptions.CoverLetterTemplate;

        try
        {
            var renderResult = await _coverLetterTemplateRenderer.RenderAsync(applicationJson, cancellationToken);
            var htmlArtifactPath = await SaveTextArtifactAsync(
                runDirectory,
                templateOptions.OutputDirectoryName,
                templateOptions.RenderedHtmlFileName,
                renderResult.HtmlDocument,
                formatAsJson: false,
                CancellationToken.None);
            var cssArtifactPath = await SaveTextArtifactAsync(
                runDirectory,
                templateOptions.OutputDirectoryName,
                templateOptions.RenderedCssFileName,
                renderResult.StylesheetText,
                formatAsJson: false,
                CancellationToken.None);
            string? pdfArtifactPath = null;
            int? pdfPageCount = null;
            bool? withinSinglePageLimit = null;
            string? pdfErrorMessage = null;
            var warnings = new List<string>(renderResult.Warnings);

            try
            {
                var pdfRenderResult = await _coverLetterPdfRenderer.RenderAsync(applicationJson, cancellationToken);
                pdfArtifactPath = await SaveBinaryArtifactAsync(
                    runDirectory,
                    templateOptions.OutputDirectoryName,
                    templateOptions.RenderedPdfFileName,
                    pdfRenderResult.PdfDocument,
                    CancellationToken.None);
                pdfPageCount = pdfRenderResult.PageCount;
                withinSinglePageLimit = pdfRenderResult.WithinSinglePageLimit;

                if (!pdfRenderResult.WithinSinglePageLimit)
                {
                    warnings.Add("PDF generation completed, but the rendered document exceeded the one-page limit.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Cover-letter PDF generation failed after HTML rendering.");
                pdfErrorMessage = exception.Message;
                warnings.Add("PDF generation failed, so no one-page PDF artifact was persisted for this run.");
            }

            var summary = new CoverLetterRenderArtifact
            {
                Status = renderResult.WithinMainContentLimit
                    && warnings.Count == 0
                    && renderResult.MissingFields.Count == 0
                    && string.IsNullOrWhiteSpace(pdfErrorMessage)
                        ? "rendered"
                        : "rendered_with_warnings",
                HtmlArtifactPath = htmlArtifactPath,
                CssArtifactPath = cssArtifactPath,
                PdfArtifactPath = pdfArtifactPath,
                PdfPageCount = pdfPageCount,
                WithinSinglePageLimit = withinSinglePageLimit,
                MainContentCharacterCount = renderResult.MainContentCharacterCount,
                MainContentBudgetUsage = renderResult.MainContentBudgetUsage,
                MaxMainContentCharacters = renderResult.MaxMainContentCharacters,
                ExplicitLineBreakCount = renderResult.ExplicitLineBreakCount,
                ParagraphBreakCount = renderResult.ParagraphBreakCount,
                EstimatedCharactersPerLine = renderResult.EstimatedCharactersPerLine,
                WithinMainContentLimit = renderResult.WithinMainContentLimit,
                MissingFields = renderResult.MissingFields,
                Warnings = warnings,
                PdfErrorMessage = pdfErrorMessage,
                ErrorMessage = null
            };

            await SaveArtifactAsync(
                runDirectory,
                templateOptions.OutputDirectoryName,
                templateOptions.RenderSummaryFileName,
                summary,
                CancellationToken.None);

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Cover-letter rendering failed after application generation.");

            var failedSummary = new CoverLetterRenderArtifact
            {
                Status = "failed",
                HtmlArtifactPath = null,
                CssArtifactPath = null,
                PdfArtifactPath = null,
                PdfPageCount = null,
                WithinSinglePageLimit = null,
                MainContentCharacterCount = null,
                MainContentBudgetUsage = null,
                MaxMainContentCharacters = templateOptions.MaxMainContentCharacters,
                ExplicitLineBreakCount = null,
                ParagraphBreakCount = null,
                EstimatedCharactersPerLine = templateOptions.EstimatedCharactersPerLine,
                WithinMainContentLimit = null,
                MissingFields = [],
                Warnings = [],
                PdfErrorMessage = null,
                ErrorMessage = exception.Message
            };

            await SaveArtifactAsync(
                runDirectory,
                templateOptions.OutputDirectoryName,
                templateOptions.RenderSummaryFileName,
                failedSummary,
                CancellationToken.None);

            return failedSummary;
        }
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

        await File.WriteAllTextAsync(destinationPath, json + Environment.NewLine, JsonSerializationDefaults.Utf8NoBom, cancellationToken);

        _logger.LogInformation("Saved artifact to {ResultFile}.", destinationRelativePath);
        return destinationRelativePath;
    }

    private async Task<RunTokenUsageReport> BuildTokenUsageReportAsync(
        string runDirectoryRelativePath,
        string jobListingFileName,
        string pipelineStatus,
        TokenPricingConfigurationSnapshot pricing,
        IReadOnlyList<LlmInteractionUsageRecord> interactions,
        CancellationToken cancellationToken)
    {
        var requestedDisplayCurrency = NormalizePricingCurrency(_openAiOptions.DisplayCurrency);
        var reportCurrency = pricing.Currency;
        var shouldConvertCosts = false;
        var currencyExchange = await _currencyDisplayConversionService.GetDisplayCurrencyQuoteAsync(pricing.Currency, cancellationToken);

        if (string.Equals(pricing.Currency, requestedDisplayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            reportCurrency = requestedDisplayCurrency;
        }
        else if (currencyExchange.AppliedRate.HasValue)
        {
            reportCurrency = requestedDisplayCurrency;
            shouldConvertCosts = true;

            if (currencyExchange.UsingStaleRate)
            {
                _logger.LogWarning(
                    "Display-currency conversion from {SourceCurrency} to {DisplayCurrency} is using stale exchange-rate data last refreshed at {LastSuccessfulRefreshUtc}.",
                    pricing.Currency,
                    requestedDisplayCurrency,
                    currencyExchange.LastSuccessfulRefreshUtc);
            }
        }
        else
        {
            _logger.LogWarning(
                "Display-currency conversion from {SourceCurrency} to {DisplayCurrency} is unavailable. Token usage will remain in {SourceCurrency} for this run report.",
                pricing.Currency,
                requestedDisplayCurrency,
                pricing.Currency);
        }

        var totals = SumTokenUsage(interactions.Select(interaction => interaction.TokenUsage));
        var interactionSummaries = new List<InteractionTokenUsageSummary>(interactions.Count);
        foreach (var interaction in interactions)
        {
            var rawEstimatedCost = BuildTokenCostSummary(interaction.TokenUsage, interaction.Pricing);
            var estimatedCost = shouldConvertCosts
                ? ConvertTokenCostSummary(rawEstimatedCost, currencyExchange)
                : rawEstimatedCost;

            interactionSummaries.Add(new InteractionTokenUsageSummary(
                Phase: interaction.Phase,
                SequenceKind: interaction.SequenceKind,
                Attempt: interaction.Attempt,
                Model: interaction.Model,
                ResponseId: interaction.ResponseId,
                Tokens: CloneTokenUsage(interaction.TokenUsage),
                Pricing: interaction.Pricing,
                EstimatedCost: estimatedCost));
        }

        var phaseTotals = interactionSummaries
            .GroupBy(interaction => interaction.Phase, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedInteractions = group.ToList();
                var groupTotals = SumTokenUsage(groupedInteractions.Select(interaction => interaction.Tokens));
                return new PhaseTokenUsageSummary(
                    Phase: group.Key,
                    Model: ResolveGroupModel(groupedInteractions.Select(interaction => interaction.Model)),
                    InteractionCount: groupedInteractions.Count,
                    Totals: groupTotals,
                    EstimatedCost: SumTokenCostSummaries(groupedInteractions.Select(interaction => interaction.EstimatedCost), reportCurrency));
            })
            .ToList();
        var sequenceTotals = interactionSummaries
            .GroupBy(interaction => (interaction.Phase, interaction.SequenceKind))
            .OrderBy(group => group.Key.Phase, StringComparer.Ordinal)
            .ThenBy(group => group.Key.SequenceKind, StringComparer.Ordinal)
            .Select(group =>
            {
                var groupedInteractions = group.ToList();
                var groupTotals = SumTokenUsage(groupedInteractions.Select(interaction => interaction.Tokens));
                return new SequenceTokenUsageSummary(
                    Phase: group.Key.Phase,
                    SequenceKind: group.Key.SequenceKind,
                    Model: ResolveGroupModel(groupedInteractions.Select(interaction => interaction.Model)),
                    InteractionCount: groupedInteractions.Count,
                    Totals: groupTotals,
                    EstimatedCost: SumTokenCostSummaries(groupedInteractions.Select(interaction => interaction.EstimatedCost), reportCurrency));
            })
            .ToList();

        return new RunTokenUsageReport(
            RunDirectory: runDirectoryRelativePath,
            JobListingFileName: jobListingFileName,
            PipelineStatus: pipelineStatus,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Model: ResolveRunModel(interactions),
            InteractionCount: interactions.Count,
            RequestedDisplayCurrency: requestedDisplayCurrency,
            DisplayCurrency: reportCurrency,
            CurrencyExchange: currencyExchange,
            Pricing: pricing,
            Totals: totals,
            EstimatedCost: SumTokenCostSummaries(interactionSummaries.Select(interaction => interaction.EstimatedCost), reportCurrency),
            PhaseTotals: phaseTotals,
            SequenceTotals: sequenceTotals,
            Interactions: interactionSummaries);
    }

    private static TokenCostSummary ConvertTokenCostSummary(TokenCostSummary summary, CurrencyExchangeRateQuote currencyExchange)
    {
        var displayCurrency = currencyExchange.TargetCurrency;
        if (string.Equals(summary.Currency, displayCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return summary with { Currency = displayCurrency };
        }

        if (!summary.PricingConfigured)
        {
            return summary with { Currency = displayCurrency };
        }

        if (!currencyExchange.AppliedRate.HasValue)
        {
            return summary;
        }

        decimal? inputCost = summary.InputCost.HasValue
            ? ConvertCost(summary.InputCost.Value, currencyExchange.AppliedRate.Value)
            : null;
        decimal? outputCost = summary.OutputCost.HasValue
            ? ConvertCost(summary.OutputCost.Value, currencyExchange.AppliedRate.Value)
            : null;
        decimal? totalCost = summary.TotalCost.HasValue
            ? ConvertCost(summary.TotalCost.Value, currencyExchange.AppliedRate.Value)
            : null;

        return CreateTokenCostSummary(
            Currency: displayCurrency,
            PricingConfigured: summary.PricingConfigured,
            InputCost: inputCost,
            OutputCost: outputCost,
            TotalCost: totalCost);
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

        await File.WriteAllTextAsync(destinationPath, outputContent + Environment.NewLine, JsonSerializationDefaults.Utf8NoBom, cancellationToken);

        _logger.LogInformation("Saved artifact to {ResultFile}.", destinationRelativePath);
        return destinationRelativePath;
    }

    private async Task<string> SaveBinaryArtifactAsync(
        string runDirectory,
        string subDirectoryName,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var destinationDirectory = Path.Combine(runDirectory, subDirectoryName);
        Directory.CreateDirectory(destinationDirectory);

        var destinationPath = Path.Combine(destinationDirectory, fileName);
        var destinationRelativePath = Path.GetRelativePath(GetRepositoryRoot(), destinationPath);

        await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);

        _logger.LogInformation("Saved artifact to {ResultFile}.", destinationRelativePath);
        return destinationRelativePath;
    }

    private TokenPricingConfigurationSnapshot BuildPricingConfigurationSnapshot()
    {
        var defaultModel = _openAiOptions.ResolveModelEntry();
        var defaultPricing = BuildTokenPricingSnapshot(
            defaultModel.Id,
            defaultModel.InputCostPerMillionTokens ?? _openAiOptions.InputCostPerMillionTokens,
            defaultModel.CachedInputCostPerMillionTokens ?? _openAiOptions.CachedInputCostPerMillionTokens,
            defaultModel.OutputCostPerMillionTokens ?? _openAiOptions.OutputCostPerMillionTokens);

        return new TokenPricingConfigurationSnapshot(
            Currency: defaultPricing.Currency,
            Default: defaultPricing,
            Phases:
            [
                new PhasePricingConfigurationSnapshot("company_context", ResolvePhaseExecutionSettings("company_context").Pricing),
                new PhasePricingConfigurationSnapshot("requirements", ResolvePhaseExecutionSettings("requirements").Pricing),
                new PhasePricingConfigurationSnapshot("candidate_evidence", ResolvePhaseExecutionSettings("candidate_evidence").Pricing),
                new PhasePricingConfigurationSnapshot("matching", ResolvePhaseExecutionSettings("matching").Pricing),
                new PhasePricingConfigurationSnapshot("application_generation", ResolvePhaseExecutionSettings("application_generation").Pricing)
            ]);
    }

    private static void RecordLlmInteraction(
        ICollection<LlmInteractionUsageRecord> interactions,
        string phase,
        string sequenceKind,
        int? attempt,
        StructuredJsonGenerationResult generationResult,
        TokenPricingSnapshot pricing)
    {
        interactions.Add(new LlmInteractionUsageRecord(
            Phase: phase,
            SequenceKind: sequenceKind,
            Attempt: attempt,
            Model: generationResult.Model,
            ResponseId: generationResult.ResponseId,
            TokenUsage: CloneTokenUsage(generationResult.TokenUsage),
            Pricing: pricing));
    }

    private OpenAiPhaseExecutionSettings ResolvePhaseExecutionSettings(string phase)
    {
        var phaseOptions = ResolvePhaseOptions(phase);
        var selectedModel = _openAiOptions.ResolveModelEntry(phaseOptions.Model);
        var model = selectedModel.Id.Trim();

        return new OpenAiPhaseExecutionSettings(
            Phase: phase,
            Model: model,
            Pricing: BuildTokenPricingSnapshot(
                model,
                phaseOptions.InputCostPerMillionTokens ?? selectedModel.InputCostPerMillionTokens ?? _openAiOptions.InputCostPerMillionTokens,
                phaseOptions.CachedInputCostPerMillionTokens ?? selectedModel.CachedInputCostPerMillionTokens ?? _openAiOptions.CachedInputCostPerMillionTokens,
                phaseOptions.OutputCostPerMillionTokens ?? selectedModel.OutputCostPerMillionTokens ?? _openAiOptions.OutputCostPerMillionTokens));
    }

    private OpenAIPhaseExecutionOptions ResolvePhaseOptions(string phase)
    {
        return phase switch
        {
            "company_context" => _openAiOptions.Phases.CompanyContext,
            "requirements" => _openAiOptions.Phases.Requirements,
            "candidate_evidence" => _openAiOptions.Phases.CandidateEvidence,
            "matching" => _openAiOptions.Phases.Matching,
            "application_generation" => _openAiOptions.Phases.ApplicationGeneration,
            _ => new OpenAIPhaseExecutionOptions()
        };
    }

    private static List<string> ResolveCompanyContextApplicantProfileFiles(SampleDataContext sampleData)
    {
        var prioritizedFiles = sampleData.PersonFiles
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                return fileName.Contains("profile", StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains("profil", StringComparison.OrdinalIgnoreCase)
                    || fileName.Contains("cv", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        return prioritizedFiles.Count > 0
            ? prioritizedFiles
            : sampleData.PersonFiles.ToList();
    }

    private TokenPricingSnapshot BuildTokenPricingSnapshot(
        string model,
        decimal? inputCostPerMillionTokens,
        decimal? cachedInputCostPerMillionTokens,
        decimal? outputCostPerMillionTokens)
    {
        var normalizedInputCost = NormalizeNonNegativePrice(inputCostPerMillionTokens);
        var normalizedCachedInputCost = NormalizeNonNegativePrice(cachedInputCostPerMillionTokens) ?? normalizedInputCost;
        var normalizedOutputCost = NormalizeNonNegativePrice(outputCostPerMillionTokens);

        return new TokenPricingSnapshot(
            Model: string.IsNullOrWhiteSpace(model) ? _openAiOptions.ResolveModelId() : model.Trim(),
            Currency: NormalizePricingCurrency(_openAiOptions.PricingCurrency),
            InputCostPerMillionTokens: normalizedInputCost,
            CachedInputCostPerMillionTokens: normalizedCachedInputCost,
            OutputCostPerMillionTokens: normalizedOutputCost,
            PricingConfigured: normalizedInputCost.HasValue && normalizedOutputCost.HasValue);
    }

    private TokenCostSummary BuildTokenCostSummary(LlmTokenUsage tokenUsage, TokenPricingSnapshot pricing)
    {
        if (!pricing.PricingConfigured || !pricing.InputCostPerMillionTokens.HasValue || !pricing.OutputCostPerMillionTokens.HasValue)
        {
            return CreateTokenCostSummary(pricing.Currency, PricingConfigured: false, InputCost: null, OutputCost: null, TotalCost: null);
        }

        var cachedInputTokens = Math.Max(0L, Math.Min(tokenUsage.CachedInputTokens, tokenUsage.InputTokens));
        var uncachedInputTokens = Math.Max(0L, tokenUsage.InputTokens - cachedInputTokens);
        var cachedInputPrice = pricing.CachedInputCostPerMillionTokens ?? pricing.InputCostPerMillionTokens.Value;
        var inputCost = RoundCost(
            uncachedInputTokens / 1_000_000m * pricing.InputCostPerMillionTokens.Value
            + cachedInputTokens / 1_000_000m * cachedInputPrice);
        var outputCost = RoundCost(tokenUsage.OutputTokens / 1_000_000m * pricing.OutputCostPerMillionTokens.Value);

        return CreateTokenCostSummary(
            Currency: pricing.Currency,
            PricingConfigured: true,
            InputCost: inputCost,
            OutputCost: outputCost,
            TotalCost: RoundCost(inputCost + outputCost));
    }

    private static TokenCostSummary SumTokenCostSummaries(IEnumerable<TokenCostSummary> summaries, string currency)
    {
        var summaryList = summaries.ToList();
        if (summaryList.Count == 0
            || summaryList.Any(summary => !summary.PricingConfigured
                || !summary.InputCost.HasValue
                || !summary.OutputCost.HasValue
                || !string.Equals(summary.Currency, currency, StringComparison.OrdinalIgnoreCase)))
        {
            return CreateTokenCostSummary(currency, PricingConfigured: false, InputCost: null, OutputCost: null, TotalCost: null);
        }

        var inputCost = RoundCost(summaryList.Sum(summary => summary.InputCost!.Value));
        var outputCost = RoundCost(summaryList.Sum(summary => summary.OutputCost!.Value));

        return CreateTokenCostSummary(
            Currency: currency,
            PricingConfigured: true,
            InputCost: inputCost,
            OutputCost: outputCost,
            TotalCost: RoundCost(inputCost + outputCost));
    }

    private static LlmTokenUsage SumTokenUsage(IEnumerable<LlmTokenUsage> usages)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long totalTokens = 0;
        long cachedInputTokens = 0;
        long reasoningTokens = 0;

        foreach (var usage in usages)
        {
            inputTokens += usage.InputTokens;
            outputTokens += usage.OutputTokens;
            totalTokens += usage.TotalTokens;
            cachedInputTokens += usage.CachedInputTokens;
            reasoningTokens += usage.ReasoningTokens;
        }

        return new LlmTokenUsage
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = totalTokens,
            CachedInputTokens = cachedInputTokens,
            ReasoningTokens = reasoningTokens
        };
    }

    private static LlmTokenUsage CloneTokenUsage(LlmTokenUsage tokenUsage)
    {
        return new LlmTokenUsage
        {
            InputTokens = tokenUsage.InputTokens,
            OutputTokens = tokenUsage.OutputTokens,
            TotalTokens = tokenUsage.TotalTokens,
            CachedInputTokens = tokenUsage.CachedInputTokens,
            ReasoningTokens = tokenUsage.ReasoningTokens
        };
    }

    private static string ResolveRunModel(IReadOnlyList<LlmInteractionUsageRecord> interactions)
    {
        return ResolveGroupModel(interactions.Select(interaction => interaction.Model));
    }

    private static string ResolveGroupModel(IEnumerable<string> models)
    {
        var distinctModels = models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinctModels.Count switch
        {
            0 => string.Empty,
            1 => distinctModels[0],
            _ => "mixed"
        };
    }

    private static decimal? NormalizeNonNegativePrice(decimal? price)
    {
        return price.HasValue && price.Value >= 0m ? price : null;
    }

    private static string NormalizePricingCurrency(string? currency)
    {
        return CurrencyCodeHelper.Normalize(currency);
    }

    private static decimal RoundCost(decimal value)
    {
        return decimal.Round(value, 8, MidpointRounding.AwayFromZero);
    }

    private static TokenCostSummary CreateTokenCostSummary(
        string Currency,
        bool PricingConfigured,
        decimal? InputCost,
        decimal? OutputCost,
        decimal? TotalCost)
    {
        decimal? roundedUpTotalCostNumeric = TotalCost.HasValue
            ? RoundUpCostToTwoDecimals(TotalCost.Value)
            : null;

        return new TokenCostSummary(
            Currency: Currency,
            PricingConfigured: PricingConfigured,
            InputCost: InputCost,
            OutputCost: OutputCost,
            TotalCost: TotalCost,
            RoundedUpTotalCost: roundedUpTotalCostNumeric.HasValue ? FormatCostToTwoDecimals(roundedUpTotalCostNumeric.Value) : null,
            RoundedUpTotalCostNumeric: roundedUpTotalCostNumeric);
    }

    private static decimal RoundUpCostToTwoDecimals(decimal value)
    {
        var scaledValue = value * 100m;
        var roundedScaledValue = value >= 0m
            ? decimal.Ceiling(scaledValue)
            : decimal.Floor(scaledValue);

        return decimal.Round(roundedScaledValue / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static string FormatCostToTwoDecimals(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static decimal ConvertCost(decimal amount, decimal exchangeRate)
    {
        return decimal.Round(amount * exchangeRate, 8, MidpointRounding.AwayFromZero);
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
        return JsonSerializationDefaults.FormatJson(json);
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
            CoverLetterStatus = executionResult.CoverLetter?.Status,
            PdfGenerated = !string.IsNullOrWhiteSpace(executionResult.CoverLetter?.PdfArtifactPath),
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
        var defaultFitStrategy = BuildDefaultFitStrategy();

        if (string.IsNullOrWhiteSpace(preferencesFilePath) || !File.Exists(preferencesFilePath))
        {
            return defaultFitStrategy;
        }

        var preferencesJson = await File.ReadAllTextAsync(preferencesFilePath, cancellationToken);
        using var document = JsonDocument.Parse(preferencesJson);
        if (!document.RootElement.TryGetProperty("fit_strategy", out var fitStrategy) || fitStrategy.ValueKind != JsonValueKind.Object)
        {
            return defaultFitStrategy;
        }

        var configuredGuidanceMode = GetString(fitStrategy, "guidance_mode");
        var guidanceMode = string.IsNullOrWhiteSpace(configuredGuidanceMode)
            ? defaultFitStrategy.GuidanceMode
            : NormalizeGuidanceMode(configuredGuidanceMode);
        var includeFitAdvisory = GetBoolean(fitStrategy, "include_fit_advisory", defaultValue: defaultFitStrategy.IncludeFitAdvisory);
        var allowApplicationOnWeakMatch = GetBoolean(fitStrategy, "allow_application_on_weak_match", defaultValue: defaultFitStrategy.AllowApplicationOnWeakMatch);
        var preferTransferableStrengthsWhenDirectMatchIsWeak = GetBoolean(
            fitStrategy,
            "prefer_transferable_strengths_when_direct_match_is_weak",
            defaultValue: defaultFitStrategy.PreferTransferableStrengthsWhenDirectMatchIsWeak);
        var allowStretchPositioning = GetBoolean(fitStrategy, "allow_stretch_positioning", defaultValue: defaultFitStrategy.AllowStretchPositioning);

        return new FitStrategyPreferences(
            guidanceMode,
            includeFitAdvisory,
            allowApplicationOnWeakMatch,
            preferTransferableStrengthsWhenDirectMatchIsWeak,
            allowStretchPositioning);
    }

    private FitStrategyPreferences BuildDefaultFitStrategy()
    {
        var defaults = _samplePipelineOptions.DefaultFitStrategy;

        return new FitStrategyPreferences(
            GuidanceMode: NormalizeGuidanceMode(defaults.GuidanceMode),
            IncludeFitAdvisory: defaults.IncludeFitAdvisory,
            AllowApplicationOnWeakMatch: defaults.AllowApplicationOnWeakMatch,
            PreferTransferableStrengthsWhenDirectMatchIsWeak: defaults.PreferTransferableStrengthsWhenDirectMatchIsWeak,
            AllowStretchPositioning: defaults.AllowStretchPositioning);
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

    private static int? TryParseRunNumber(string? directoryName, string prefix)
    {
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
        string CandidateDirectoryName,
        string JobListingFileName,
        string RunDirectory,
        string RunDirectoryRelativePath,
        string? MatchingJson,
        string? ApplicationJson,
        PipelineVerificationSummary? VerificationSummary,
        FitAdvisorySummary? FitAdvisory,
        CoverLetterRenderArtifact? CoverLetter);

    private sealed record RequirementsStageRecoveryOutcome(string RequirementsJson, StageVerificationResult StageResult);

    private sealed record MatchingStageRecoveryOutcome(string MatchingJson, StageVerificationResult StageResult);

    private sealed record ApplicationGenerationStageRecoveryOutcome(string ApplicationJson, StageVerificationResult StageResult);

    private sealed record LlmInteractionUsageRecord(
        string Phase,
        string SequenceKind,
        int? Attempt,
        string Model,
        string? ResponseId,
        LlmTokenUsage TokenUsage,
        TokenPricingSnapshot Pricing);

    private sealed record RunTokenUsageReport(
        string RunDirectory,
        string JobListingFileName,
        string PipelineStatus,
        DateTimeOffset RecordedAtUtc,
        string Model,
        int InteractionCount,
        string RequestedDisplayCurrency,
        string DisplayCurrency,
        CurrencyExchangeRateQuote CurrencyExchange,
        TokenPricingConfigurationSnapshot Pricing,
        LlmTokenUsage Totals,
        TokenCostSummary EstimatedCost,
        IReadOnlyList<PhaseTokenUsageSummary> PhaseTotals,
        IReadOnlyList<SequenceTokenUsageSummary> SequenceTotals,
        IReadOnlyList<InteractionTokenUsageSummary> Interactions);

    private sealed record TokenPricingConfigurationSnapshot(
        string Currency,
        TokenPricingSnapshot Default,
        IReadOnlyList<PhasePricingConfigurationSnapshot> Phases);

    private sealed record PhasePricingConfigurationSnapshot(
        string Phase,
        TokenPricingSnapshot Pricing);

    private sealed record TokenPricingSnapshot(
        string Model,
        string Currency,
        decimal? InputCostPerMillionTokens,
        decimal? CachedInputCostPerMillionTokens,
        decimal? OutputCostPerMillionTokens,
        bool PricingConfigured);

    private sealed record TokenCostSummary(
        string Currency,
        bool PricingConfigured,
        decimal? InputCost,
        decimal? OutputCost,
        decimal? TotalCost,
        string? RoundedUpTotalCost,
        decimal? RoundedUpTotalCostNumeric);

    private sealed record PhaseTokenUsageSummary(
        string Phase,
        string Model,
        int InteractionCount,
        LlmTokenUsage Totals,
        TokenCostSummary EstimatedCost);

    private sealed record SequenceTokenUsageSummary(
        string Phase,
        string SequenceKind,
        string Model,
        int InteractionCount,
        LlmTokenUsage Totals,
        TokenCostSummary EstimatedCost);

    private sealed record InteractionTokenUsageSummary(
        string Phase,
        string SequenceKind,
        int? Attempt,
        string Model,
        string? ResponseId,
        LlmTokenUsage Tokens,
        TokenPricingSnapshot Pricing,
        TokenCostSummary EstimatedCost);

    private sealed record OpenAiPhaseExecutionSettings(
        string Phase,
        string Model,
        TokenPricingSnapshot Pricing);

    private sealed record FitStrategyPreferences(
        string GuidanceMode,
        bool IncludeFitAdvisory,
        bool AllowApplicationOnWeakMatch,
        bool PreferTransferableStrengthsWhenDirectMatchIsWeak,
        bool AllowStretchPositioning);
}