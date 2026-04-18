namespace Backend.api.Services.ApplyAIService
{
    public interface IApplyAiJobExecutionService
    {
        Task ExecuteQueuedJobAsync(Guid jobId, CancellationToken cancellationToken = default);
    }
}