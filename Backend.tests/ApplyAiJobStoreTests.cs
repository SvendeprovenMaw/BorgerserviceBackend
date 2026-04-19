using ApplyAI.LlmPipeline;
using Backend.api.Database;
using Backend.api.Services.ApplyAIService;
using Backend.tests.TestSupport;
using FluentAssertions;

namespace Backend.tests;

public sealed class ApplyAiJobStoreTests
{
    [Fact]
    public void Add_TracksANewPipelineJobAggregate()
    {
        using var db = ApplyAiDbContextFactory.CreateInMemory();
        var user = ApplyAiTestData.CreateUser();
        db.Users.Add(user);
        db.SaveChanges();

        var job = new ApplyAiPipelineJobBuilder(user).WithId(Guid.NewGuid()).Build();
        var store = new ApplyAiJobStore(db);

        store.Add(job);
        store.SaveChangesAsync().GetAwaiter().GetResult();

        db.ApplyAiPipelineJobs.Should().ContainSingle(item => item.Id == job.Id);
    }

    [Fact]
    public async Task GetAsync_LoadsPhaseStatesArtifactsAndEvents()
    {
        using var db = ApplyAiDbContextFactory.CreateInMemory();
        var user = ApplyAiTestData.CreateUser();
        db.Users.Add(user);

        var job = new ApplyAiPipelineJobBuilder(user)
            .WithId(Guid.NewGuid())
            .WithStoredJobPostingArtifact()
            .WithEvent(PipelineEventType.JobAccepted, DateTimeOffset.UtcNow, "evt-1", "Accepted")
            .Build();

        db.ApplyAiPipelineJobs.Add(job);
        await db.SaveChangesAsync();

        var store = new ApplyAiJobStore(db);
        var loaded = await store.GetAsync(job.Id);

        loaded.PhaseStates.Should().HaveCount(PipelinePhaseCatalog.All.Count);
        loaded.Artifacts.Should().NotBeEmpty();
        loaded.Events.Should().ContainSingle(item => item.EventId == "evt-1");
    }

    [Fact]
    public async Task GetAsync_ThrowsForMissingJob()
    {
        using var db = ApplyAiDbContextFactory.CreateInMemory();
        var store = new ApplyAiJobStore(db);

        var act = () => store.GetAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetOwnedAsync_RejectsInvalidGuidJobIds()
    {
        using var db = ApplyAiDbContextFactory.CreateInMemory();
        var store = new ApplyAiJobStore(db);

        var act = () => store.GetOwnedAsync("not-a-guid", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetOwnedAsync_FiltersByOwnerAndThrowsWhenJobIsNotOwned()
    {
        using var db = ApplyAiDbContextFactory.CreateInMemory();
        var owner = ApplyAiTestData.CreateUser();
        var otherUser = ApplyAiTestData.CreateUser();
        db.Users.AddRange(owner, otherUser);

        var job = new ApplyAiPipelineJobBuilder(owner).WithId(Guid.NewGuid()).Build();
        db.ApplyAiPipelineJobs.Add(job);
        await db.SaveChangesAsync();

        var store = new ApplyAiJobStore(db);
        var act = () => store.GetOwnedAsync(job.Id.ToString(), otherUser.Id);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}