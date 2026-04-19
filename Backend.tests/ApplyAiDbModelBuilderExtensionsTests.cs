using ApplyAI.LlmPipeline;
using Backend.api.Entities;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend.tests;

public sealed class ApplyAiDbModelBuilderExtensionsTests
{
    [Fact]
    public void AddApplyAiPipelineModel_MapsTablesIntoLlmPipelineSchema()
    {
        using var scope = ApplyAiDbContextFactory.CreateSqliteScope();

        scope.Db.Model.FindEntityType(typeof(ApplyAiPipelineJob))!.GetSchema().Should().Be(ApplyAiDbModelBuilderExtensions.PipelineSchema);
        scope.Db.Model.FindEntityType(typeof(ApplyAiPipelinePhaseState))!.GetSchema().Should().Be(ApplyAiDbModelBuilderExtensions.PipelineSchema);
        scope.Db.Model.FindEntityType(typeof(ApplyAiPipelineArtifact))!.GetSchema().Should().Be(ApplyAiDbModelBuilderExtensions.PipelineSchema);
        scope.Db.Model.FindEntityType(typeof(ApplyAiPipelineEvent))!.GetSchema().Should().Be(ApplyAiDbModelBuilderExtensions.PipelineSchema);
    }

    [Fact]
    public async Task SentApplication_EnforcesUniqueIndexOnPipelineJobId()
    {
        await using var scope = ApplyAiDbContextFactory.CreateSqliteScope();
        var user = ApplyAiTestData.CreateUser();
        scope.Db.Users.Add(user);

        var first = new SentApplication
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            PipelineJobId = Guid.NewGuid(),
            Company = "ApplyAI",
            Position = "Backend Developer",
            JobPosting = "Job posting",
            Status = "sent",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SentAtUtc = DateTimeOffset.UtcNow,
            ApplicantName = "Test Applicant",
            SubjectLine = "Subject",
            FinalText = "Final text",
        };

        scope.Db.SentApplications.Add(first);
        await scope.Db.SaveChangesAsync();

        scope.Db.SentApplications.Add(new SentApplication
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            User = user,
            PipelineJobId = first.PipelineJobId,
            Company = "ApplyAI",
            Position = "Backend Developer",
            JobPosting = "Job posting",
            Status = "sent",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            SentAtUtc = DateTimeOffset.UtcNow,
            ApplicantName = "Test Applicant",
            SubjectLine = "Subject",
            FinalText = "Different final text",
        });

        var act = () => scope.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ApplyAiPipelinePhaseState_EnforcesUniqueIndexOnJobIdAndPhase()
    {
        await using var scope = ApplyAiDbContextFactory.CreateSqliteScope();
        var user = ApplyAiTestData.CreateUser();
        scope.Db.Users.Add(user);

        var job = new ApplyAiPipelineJobBuilder(user).WithId(Guid.NewGuid()).Build();
        scope.Db.ApplyAiPipelineJobs.Add(job);
        await scope.Db.SaveChangesAsync();

        scope.Db.ApplyAiPipelinePhaseStates.Add(new ApplyAiPipelinePhaseState
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            Job = job,
            Phase = PipelinePhase.CompanyContext,
            Status = PipelinePhaseStatus.Pending,
            StatusMessage = "Duplicate phase",
        });

        var act = () => scope.Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task ApplyAiPipelineJob_CascadesDeletesToPhaseStatesArtifactsAndEvents()
    {
        await using var scope = ApplyAiDbContextFactory.CreateSqliteScope();
        var user = ApplyAiTestData.CreateUser();
        scope.Db.Users.Add(user);

        var job = new ApplyAiPipelineJobBuilder(user)
            .WithId(Guid.NewGuid())
            .WithStoredJobPostingArtifact()
            .WithEvent(PipelineEventType.JobAccepted, DateTimeOffset.UtcNow, "evt-1", "Accepted")
            .Build();

        scope.Db.ApplyAiPipelineJobs.Add(job);
        await scope.Db.SaveChangesAsync();

        scope.Db.ApplyAiPipelineJobs.Remove(job);
        await scope.Db.SaveChangesAsync();

        scope.Db.ApplyAiPipelinePhaseStates.Should().BeEmpty();
        scope.Db.ApplyAiPipelineArtifacts.Should().BeEmpty();
        scope.Db.ApplyAiPipelineEvents.Should().BeEmpty();
    }
}