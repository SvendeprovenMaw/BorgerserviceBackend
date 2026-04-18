using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

namespace Backend.api.Services.ApplyAIService
{
    public interface IApplyAiExecutionQueue
    {
        ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default);
    }

    public sealed class ApplyAiExecutionQueue : BackgroundService, IApplyAiExecutionQueue
    {
        private readonly Channel<Guid> _jobQueue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        private readonly ILogger<ApplyAiExecutionQueue> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        public ApplyAiExecutionQueue(IServiceScopeFactory scopeFactory, ILogger<ApplyAiExecutionQueue> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            return _jobQueue.Writer.WriteAsync(jobId, cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var jobId in _jobQueue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var executor = scope.ServiceProvider.GetRequiredService<IApplyAiJobExecutionService>();
                    await executor.ExecuteQueuedJobAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "ApplyAI background execution failed for queued job {JobId}.", jobId);
                }
            }
        }
    }
}