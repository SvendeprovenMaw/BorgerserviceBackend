using System.Text;
using Backend.api.Services.ApplyAIService;
using Backend.api.Services.ApplyAIService.LlmRuntime.Models;
using Backend.api.Services.ApplyAIService.LlmRuntime.Services;
using Backend.tests.TestSupport;
using FluentAssertions;
using Moq;

namespace Backend.tests;

public sealed class ApplyAiStageOneRuntimeTests
{
    [Fact]
    public async Task GenerateRequirementsAsync_SavesUploadedFileAndDelegatesToRequirementsService()
    {
        string? savedPath = null;
        var requirementsService = new Mock<IRequirementsParsingService>();
        requirementsService
            .Setup(service => service.GenerateRequirementsAsync(It.IsAny<RequirementsGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<RequirementsGenerationRequest, CancellationToken>((request, _) =>
            {
                savedPath = request.JobPostingFilePath;
                File.Exists(request.JobPostingFilePath!).Should().BeTrue();
                return Task.FromResult(ApplyAiTestData.CreateStructuredResult(new { requirements = Array.Empty<object>() }));
            });

        var runtime = CreateRuntime(requirementsService: requirementsService);

        var result = await runtime.GenerateRequirementsAsync(Encoding.UTF8.GetBytes("pdf-content"), "job-posting.pdf", "application/pdf");

        result.Should().Contain("requirements");
        savedPath.Should().NotBeNull();
        Directory.Exists(Path.GetDirectoryName(savedPath!)!).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateRequirementsAsync_FallsBackToGeneratedPdfFileNameWhenIncomingFileNameIsBlank()
    {
        string? savedPath = null;
        var requirementsService = new Mock<IRequirementsParsingService>();
        requirementsService
            .Setup(service => service.GenerateRequirementsAsync(It.IsAny<RequirementsGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<RequirementsGenerationRequest, CancellationToken>((request, _) =>
            {
                savedPath = request.JobPostingFilePath;
                return Task.FromResult(ApplyAiTestData.CreateStructuredResult(new { requirements = Array.Empty<object>() }));
            });

        var runtime = CreateRuntime(requirementsService: requirementsService);

        await runtime.GenerateRequirementsAsync(Encoding.UTF8.GetBytes("pdf-content"), string.Empty, "application/pdf");

        Path.GetFileName(savedPath!).Should().EndWith(".pdf");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateRequirementsAsync_CleansUpTemporaryDirectoryOnSuccessAndFailure(bool shouldThrow)
    {
        string? savedPath = null;
        var requirementsService = new Mock<IRequirementsParsingService>();
        requirementsService
            .Setup(service => service.GenerateRequirementsAsync(It.IsAny<RequirementsGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<RequirementsGenerationRequest, CancellationToken>((request, _) =>
            {
                savedPath = request.JobPostingFilePath;
                if (shouldThrow)
                {
                    throw new InvalidOperationException("requirements failed");
                }

                return Task.FromResult(ApplyAiTestData.CreateStructuredResult(new { requirements = Array.Empty<object>() }));
            });

        var runtime = CreateRuntime(requirementsService: requirementsService);
        var act = () => runtime.GenerateRequirementsAsync(Encoding.UTF8.GetBytes("pdf-content"), "job-posting.pdf", "application/pdf");

        if (shouldThrow)
        {
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await act();
        }

        Directory.Exists(Path.GetDirectoryName(savedPath!)!).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateCompanyContextAsync_ForwardsCompanyNameApplicantProfileAndAddressHint()
    {
        CompanyContextGenerationRequest? capturedRequest = null;
        var companyContextService = new Mock<ICompanyContextService>();
        companyContextService
            .Setup(service => service.GenerateCompanyContextAsync(It.IsAny<CompanyContextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<CompanyContextGenerationRequest, CancellationToken>((request, _) =>
            {
                capturedRequest = request;
                return Task.FromResult(ApplyAiTestData.CreateStructuredResult(new { company_profile = new { industry_da = "Public" } }));
            });

        var runtime = CreateRuntime(companyContextService: companyContextService);

        await runtime.GenerateCompanyContextAsync(
            Encoding.UTF8.GetBytes("pdf-content"),
            "job-posting.pdf",
            "application/pdf",
            "Acme Kommune",
            "Applicant profile text",
            "Odense");

        capturedRequest.Should().NotBeNull();
        capturedRequest!.CompanyName.Should().Be("Acme Kommune");
        capturedRequest.ApplicantProfileText.Should().Be("Applicant profile text");
        capturedRequest.ApplicantAddressHint.Should().Be("Odense");
        capturedRequest.JobPostingFilePath.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateCompanyContextAsync_CleansUpTemporaryDirectoryOnSuccessAndFailure(bool shouldThrow)
    {
        string? savedPath = null;
        var companyContextService = new Mock<ICompanyContextService>();
        companyContextService
            .Setup(service => service.GenerateCompanyContextAsync(It.IsAny<CompanyContextGenerationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<CompanyContextGenerationRequest, CancellationToken>((request, _) =>
            {
                savedPath = request.JobPostingFilePath;
                if (shouldThrow)
                {
                    throw new InvalidOperationException("company context failed");
                }

                return Task.FromResult(ApplyAiTestData.CreateStructuredResult(new { company_profile = new { industry_da = "Public" } }));
            });

        var runtime = CreateRuntime(companyContextService: companyContextService);
        var act = () => runtime.GenerateCompanyContextAsync(
            Encoding.UTF8.GetBytes("pdf-content"),
            "job-posting.pdf",
            "application/pdf",
            "Acme Kommune",
            "Applicant profile text",
            "Odense");

        if (shouldThrow)
        {
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            await act();
        }

        Directory.Exists(Path.GetDirectoryName(savedPath!)!).Should().BeFalse();
    }

    [Fact]
    public async Task VerifyRequirementsAsync_CombinesVerificationOutputAndGateOutputIntoStageVerificationResult()
    {
        StageVerificationRequest? verificationRequest = null;
        var verificationOrchestrator = new Mock<IVerificationOrchestrator>();
        verificationOrchestrator
            .Setup(service => service.VerifyStageAsync(It.IsAny<StageVerificationRequest>(), It.IsAny<CancellationToken>()))
            .Returns<StageVerificationRequest, CancellationToken>((request, _) =>
            {
                verificationRequest = request;
                return Task.FromResult(ApplyAiTestData.CreateVerificationResult(VerificationStage.Requirements, request.DocumentId, approvedForDownstream: true));
            });

        var gateEvaluator = new Mock<IDownstreamGateEvaluator>();
        gateEvaluator
            .Setup(service => service.Evaluate(It.IsAny<StageVerificationRequest>(), It.IsAny<StageVerificationResult>()))
            .Returns(ApplyAiTestData.CreateGateResult("Requirements", approvedForDownstream: true));

        var runtime = CreateRuntime(verificationOrchestrator: verificationOrchestrator, gateEvaluator: gateEvaluator);

        var result = await runtime.VerifyRequirementsAsync("doc-1", "{\"requirements\":[]}", "job-posting.pdf");

        verificationRequest.Should().NotBeNull();
        verificationRequest!.ExpectedParsedFiles.Should().Equal("job-posting.pdf");
        result.ApprovedForDownstream.Should().BeTrue();
        result.VerificationJson.Should().Contain("\"status\"");
        result.GateJson.Should().Contain("approvedForDownstream");
    }

    private static ApplyAiStageOneRuntime CreateRuntime(
        Mock<IRequirementsParsingService>? requirementsService = null,
        Mock<ICompanyContextService>? companyContextService = null,
        Mock<IVerificationOrchestrator>? verificationOrchestrator = null,
        Mock<IDownstreamGateEvaluator>? gateEvaluator = null)
    {
        return new ApplyAiStageOneRuntime(
            (requirementsService ?? new Mock<IRequirementsParsingService>()).Object,
            (companyContextService ?? new Mock<ICompanyContextService>()).Object,
            (verificationOrchestrator ?? new Mock<IVerificationOrchestrator>()).Object,
            (gateEvaluator ?? new Mock<IDownstreamGateEvaluator>()).Object,
            new TestHostEnvironment(),
            TestConfiguration.Empty());
    }
}