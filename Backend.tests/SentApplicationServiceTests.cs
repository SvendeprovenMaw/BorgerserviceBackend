using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Entities.Dto;
using Backend.api.Services;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Moq;

namespace Backend.tests;

public sealed class SentApplicationServiceTests
{
    [Fact]
    public async Task SaveAsync_CreatesLockedApplicationSnapshotFromPipelineJob()
    {
        await using var db = ApplyAiDbContextFactory.CreateInMemory();
        var user = ApplyAiTestData.CreateUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "demo@example.com", "demo-user");
        db.Users.Add(user);

        var job = new ApplyAiPipelineJobBuilder(user)
            .WithId(Guid.NewGuid())
            .WithStatus(PipelineJobStatus.Completed)
            .Build();

        var companyContextState = job.PhaseStates.Single(item => item.Phase == PipelinePhase.CompanyContext);
        companyContextState.Status = PipelinePhaseStatus.Completed;
        companyContextState.DocumentJson = ApplyAiTestData.Json(new
        {
            generatedAtUtc = "2026-04-19T12:00:00Z",
            jobPostingSource = new
            {
                sourceType = "RemoteUrl",
                reference = "https://jobs.example.com/backend-developer",
            },
            companyContextOverrides = new
            {
                companyName = "ApplyAI Kommune",
                applicantAddressHint = "Roskilde",
            },
            storedJobPostingArtifact = new
            {
                displayName = "rendered-job-posting.pdf",
            },
            llmAssetPaths = new
            {
                promptPath = "Assets/Prompts/company-context.md",
            },
            candidateFiles = new[] { "cv.pdf", "portfolio.pdf" },
            phaseOutput = new
            {
                _meta = new
                {
                    company_name = "ApplyAI Kommune",
                    parsed_files = new[] { "backend-developer.pdf" },
                },
                company_profile = new
                {
                    industry_da = "Digital borgerservice",
                    employee_count = new { display_da = "120 medarbejdere" },
                    trustpilot = new { display_da = "4,7 / 5" },
                    glassdoor = new { display_da = "4,2 / 5" },
                    homepage_values_mission_vision_da = new[] { "Borgernær service", "Stærkt tværfagligt samarbejde" },
                },
                economics = new { growth_assessment_da = "Stabil vækst i digitale ydelser." },
                employee_conditions = new { skill_development_opportunities_da = "Mentorordning og certificeringer." },
                distance = new { calculation_basis_da = "Hybridt setup med 35 minutters transport." },
                media_mentions = new { recent_media_mentions_da = new[] { "Kommunen udvider sine AI-initiativer." } },
            },
        });

        var requirementsState = job.PhaseStates.Single(item => item.Phase == PipelinePhase.Requirements);
        requirementsState.Status = PipelinePhaseStatus.Completed;
        requirementsState.DocumentJson = ApplyAiTestData.Json(new
        {
            phaseOutput = new
            {
                requirements = new object[]
                {
                    new { importance = "must_have" },
                    new { importance = "should_have" },
                    new { importance = "nice_to_have" },
                },
            },
        });

        var applicationState = job.PhaseStates.Single(item => item.Phase == PipelinePhase.ApplicationGeneration);
        applicationState.Status = PipelinePhaseStatus.Completed;
        applicationState.DocumentJson = ApplyAiTestData.Json(new
        {
            phaseOutput = new
            {
                _meta = new
                {
                    applicant_display_name = "Emma Sørensen",
                    company_name = "ApplyAI Kommune",
                    position_title = "Backend Developer",
                },
                application_strategy = new
                {
                    subject_line_da = "Ansøgning til Backend Developer",
                },
            },
        });

        job.Artifacts.Add(new ApplyAiPipelineArtifact
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            Job = job,
            Phase = PipelinePhase.ApplicationGeneration,
            ArtifactKind = PipelineArtifactKind.PdfDocument,
            RelativePath = "cover-letter/cover-letter.pdf",
            DisplayName = "cover-letter.pdf",
            MediaType = "application/pdf",
        });

        db.ApplyAiPipelineJobs.Add(job);
        await db.SaveChangesAsync();

        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetUser(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);

        var service = new SentApplicationService(db, userService.Object);
        var result = await service.SaveAsync(
            ApplyAiTestData.CreatePrincipal(user.Id),
            new FinishedApplicationRequestDto
            {
                PipelineJobId = job.Id,
                TemplateSnapshot = new ApplicationTemplateSnapshotDto
                {
                    Id = "applyai-default",
                    Name = "ApplyAI Default",
                    PreviewTone = "paper-modern",
                },
                Sections =
                [
                    new ApplicationSectionDto { Id = "subject_line", Label = "Subject line", Text = "Ansøgning til Backend Developer", Kind = "subject_line" },
                    new ApplicationSectionDto { Id = "opening", Label = "Opening", Text = "Jeg søger stillingen, fordi jeg matcher jeres API- og integrationsbehov.", Kind = "opening" },
                    new ApplicationSectionDto { Id = "closing", Label = "Closing", Text = "Jeg ser frem til at høre fra jer.", Kind = "closing" },
                ],
            });

        result.Error.Should().Be(SaveFinishedApplicationError.None);
        result.Application.Should().NotBeNull();
        result.Application!.Company.Should().Be("ApplyAI Kommune");
        result.Application.Position.Should().Be("Backend Developer");
        result.Application.SubjectLine.Should().Be("Ansøgning til Backend Developer");
        result.Application.TemplateSnapshot.Name.Should().Be("ApplyAI Default");
        result.Application.Sections.Should().HaveCount(3);
        result.Application.GeneratedArtifacts.Should().ContainSingle(artifact => artifact.DisplayName == "cover-letter.pdf");
        result.Application.CompanyContext.Industry.Should().Be("Digital borgerservice");
        result.Application.CompanyContext.RequirementBreakdownLabel.Should().Be("1 must-have / 1 should-have / 1 nice-to-have");
        db.SentApplications.Should().ContainSingle();
    }

    [Fact]
    public async Task SaveAsync_ReturnsExistingLockedSnapshotWithoutOverwritingIt()
    {
        await using var db = ApplyAiDbContextFactory.CreateInMemory();
        var user = ApplyAiTestData.CreateUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "demo@example.com", "demo-user");
        db.Users.Add(user);

        var pipelineJobId = Guid.NewGuid();
        db.SentApplications.Add(new SentApplication
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            PipelineJobId = pipelineJobId,
            Company = "ApplyAI Kommune",
            Position = "Backend Developer",
            JobPosting = "backend-developer.pdf",
            Status = "sent",
            CreatedAtUtc = DateTimeOffset.Parse("2026-04-19T12:00:00Z"),
            SentAtUtc = DateTimeOffset.Parse("2026-04-19T13:00:00Z"),
            ApplicantName = "Emma Sørensen",
            SubjectLine = "Original subject",
            FinalText = "Original final text",
            TemplateSnapshotJson = "{}",
            SectionsJson = "[]",
            CompanyContextJson = "{}",
            GeneratedArtifactsJson = "[]",
        });
        await db.SaveChangesAsync();

        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetUser(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);

        var service = new SentApplicationService(db, userService.Object);
        var result = await service.SaveAsync(
            ApplyAiTestData.CreatePrincipal(user.Id),
            new FinishedApplicationRequestDto
            {
                PipelineJobId = pipelineJobId,
                TemplateSnapshot = new ApplicationTemplateSnapshotDto { Id = "changed", Name = "Changed" },
                Sections =
                [
                    new ApplicationSectionDto { Id = "opening", Label = "Opening", Text = "Changed text" },
                ],
            });

        result.Error.Should().Be(SaveFinishedApplicationError.None);
        result.Application!.SubjectLine.Should().Be("Original subject");
        result.Application.Final.Should().Be("Original final text");
        db.SentApplications.Should().ContainSingle();
    }

    [Fact]
    public async Task ListAsync_FiltersSentApplicationsToCurrentUser()
    {
        await using var db = ApplyAiDbContextFactory.CreateInMemory();
        var currentUser = ApplyAiTestData.CreateUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "demo@example.com", "demo-user");
        var otherUser = ApplyAiTestData.CreateUser(Guid.Parse("22222222-2222-2222-2222-222222222222"), "other@example.com", "other-user");
        db.Users.AddRange(currentUser, otherUser);
        db.SentApplications.AddRange(
            new SentApplication
            {
                Id = Guid.NewGuid(),
                UserId = currentUser.Id,
                User = currentUser,
                PipelineJobId = Guid.NewGuid(),
                Company = "ApplyAI Kommune",
                Position = "Backend Developer",
                JobPosting = "backend-developer.pdf",
                Status = "sent",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SentAtUtc = DateTimeOffset.UtcNow,
                ApplicantName = "Emma Sørensen",
                SubjectLine = "Current user",
                FinalText = "Current user final",
            },
            new SentApplication
            {
                Id = Guid.NewGuid(),
                UserId = otherUser.Id,
                User = otherUser,
                PipelineJobId = Guid.NewGuid(),
                Company = "Other Company",
                Position = "Frontend Developer",
                JobPosting = "frontend-developer.pdf",
                Status = "sent",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                SentAtUtc = DateTimeOffset.UtcNow,
                ApplicantName = "Other User",
                SubjectLine = "Other user",
                FinalText = "Other user final",
            });
        await db.SaveChangesAsync();

        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetUser(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(currentUser);

        var service = new SentApplicationService(db, userService.Object);
        var applications = await service.ListAsync(ApplyAiTestData.CreatePrincipal(currentUser.Id));

        applications.Should().ContainSingle();
        applications[0].Company.Should().Be("ApplyAI Kommune");
    }

    [Fact]
    public async Task SaveAsync_ReturnsValidationErrorWhenNoNonEmptySectionsAreProvided()
    {
        await using var db = ApplyAiDbContextFactory.CreateInMemory();
        var user = ApplyAiTestData.CreateUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), "demo@example.com", "demo-user");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var userService = new Mock<IUserService>();
        userService.Setup(service => service.GetUser(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);

        var service = new SentApplicationService(db, userService.Object);
        var result = await service.SaveAsync(
            ApplyAiTestData.CreatePrincipal(user.Id),
            new FinishedApplicationRequestDto
            {
                PipelineJobId = Guid.NewGuid(),
                TemplateSnapshot = new ApplicationTemplateSnapshotDto { Id = "applyai-default", Name = "ApplyAI Default" },
                Sections =
                [
                    new ApplicationSectionDto { Id = "opening", Label = "Opening", Text = "   " },
                ],
            });

        result.Error.Should().Be(SaveFinishedApplicationError.InvalidSections);
        result.Message.Should().NotBeNullOrWhiteSpace();
    }
}