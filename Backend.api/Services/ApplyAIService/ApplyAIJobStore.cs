using Backend.api.Database;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services.ApplyAIService
{
    public sealed class ApplyAiJobStore
    {
        private readonly ApplyAIDbContext _db;

        public ApplyAiJobStore(ApplyAIDbContext db)
        {
            _db = db;
        }

        public void Add(ApplyAiPipelineJob job)
        {
            _db.ApplyAiPipelineJobs.Add(job);
        }

        public async Task<ApplyAiPipelineJob> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            var job = await _db.ApplyAiPipelineJobs
                .AsSplitQuery()
                .Include(item => item.PhaseStates)
                .Include(item => item.Artifacts)
                .Include(item => item.Events)
                .FirstOrDefaultAsync(item => item.Id == jobId, cancellationToken);

            if (job is null)
            {
                throw new KeyNotFoundException($"Pipeline job '{jobId}' was not found.");
            }

            return job;
        }

        public async Task<ApplyAiPipelineJob> GetOwnedAsync(string jobId, Guid ownerUserId, CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(jobId, out var parsedJobId))
            {
                throw new ArgumentException("Job id must be a valid GUID.", nameof(jobId));
            }

            var job = await _db.ApplyAiPipelineJobs
                .AsSplitQuery()
                .Include(item => item.PhaseStates)
                .Include(item => item.Artifacts)
                .Include(item => item.Events)
                .FirstOrDefaultAsync(item => item.Id == parsedJobId && item.UserId == ownerUserId, cancellationToken);

            if (job is null)
            {
                throw new KeyNotFoundException("Pipeline job was not found for the current user.");
            }

            return job;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _db.SaveChangesAsync(cancellationToken);
        }
    }
}