using Backend.api.Services.ApplyAIService;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Backend.tests;

public sealed class ApplyAiExecutionQueueTests
{
    [Fact]
    public async Task QueueAsync_AndBackgroundLoop_DispatchQueuedJobIdsToExecutionService()
    {
        var processed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new Mock<IApplyAiJobExecutionService>();
        executor
            .Setup(service => service.ExecuteQueuedJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((jobId, _) =>
            {
                processed.TrySetResult(jobId);
                return Task.CompletedTask;
            });

        using var provider = new ServiceCollection()
            .AddScoped(_ => executor.Object)
            .BuildServiceProvider();

        var queue = new ApplyAiExecutionQueue(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ApplyAiExecutionQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var jobId = Guid.NewGuid();
        await queue.QueueAsync(jobId);

        (await processed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(jobId);

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueueExecution_LogsErrorsAndKeepsProcessingLaterJobs()
    {
        var processed = new List<Guid>();
        var secondProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new Mock<IApplyAiJobExecutionService>();
        executor
            .Setup(service => service.ExecuteQueuedJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((jobId, _) =>
            {
                processed.Add(jobId);
                if (processed.Count == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                secondProcessed.TrySetResult();
                return Task.CompletedTask;
            });

        var logger = new CapturingLogger<ApplyAiExecutionQueue>();
        using var provider = new ServiceCollection()
            .AddScoped(_ => executor.Object)
            .BuildServiceProvider();

        var queue = new ApplyAiExecutionQueue(provider.GetRequiredService<IServiceScopeFactory>(), logger);
        await queue.StartAsync(CancellationToken.None);
        var firstJobId = Guid.NewGuid();
        var secondJobId = Guid.NewGuid();

        await queue.QueueAsync(firstJobId);
        await queue.QueueAsync(secondJobId);

        await secondProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        processed.Should().Equal(firstJobId, secondJobId);
        logger.Errors.Should().ContainSingle(message => !message.Contains(secondJobId.ToString()));

        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QueueExecution_ExitsCleanlyOnServiceCancellation()
    {
        var executor = new Mock<IApplyAiJobExecutionService>();
        using var provider = new ServiceCollection()
            .AddScoped(_ => executor.Object)
            .BuildServiceProvider();

        var queue = new ApplyAiExecutionQueue(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ApplyAiExecutionQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        await queue.StopAsync(CancellationToken.None);
        executor.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleQueuedJobIds_AreProcessedInFifoOrder()
    {
        var processed = new List<Guid>();
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new Mock<IApplyAiJobExecutionService>();
        executor
            .Setup(service => service.ExecuteQueuedJobAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns<Guid, CancellationToken>((jobId, _) =>
            {
                processed.Add(jobId);
                if (processed.Count == 3)
                {
                    allProcessed.TrySetResult();
                }

                return Task.CompletedTask;
            });

        using var provider = new ServiceCollection()
            .AddScoped(_ => executor.Object)
            .BuildServiceProvider();

        var queue = new ApplyAiExecutionQueue(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ApplyAiExecutionQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        var jobIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var jobId in jobIds)
        {
            await queue.QueueAsync(jobId);
        }

        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        processed.Should().Equal(jobIds);

        await queue.StopAsync(CancellationToken.None);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Errors { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errors.Add(formatter(state, exception));
            }
        }
    }
}