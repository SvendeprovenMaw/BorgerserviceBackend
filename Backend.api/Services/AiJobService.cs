

using Backend.api.Database;
using Backend.api.Entities;
using Microsoft.EntityFrameworkCore;
using Openai.Library.Services;

namespace Backend.api.Services
{
    public interface IAiJobService
    {
        Task<AiProcessingJob> GetAiJobByIdAsync(Guid id, Guid userId);
        Task SaveAiJobAsync(AiProcessingJob job);
        Task UpdateAiJobAsync(AiProcessingJob job);
    }

    public class AiJobService : IAiJobService
    {
        IConfiguration _conf;
        ApplyAiDbContext _warehouseDbContext;
        public AiJobService(IConfiguration conf, ApplyAiDbContext warehouseDbContext)
        {
            this._conf = conf;
            this._warehouseDbContext = warehouseDbContext;
        }

        public async Task SaveAiJobAsync(AiProcessingJob job)
        {
            await _warehouseDbContext.AiJobs.AddAsync(job);
            await _warehouseDbContext.SaveChangesAsync();
        }

        public async Task UpdateAiJobAsync(AiProcessingJob job)
        {
            _warehouseDbContext.AiJobs.Update(job);
            await _warehouseDbContext.SaveChangesAsync();
        }
        public async Task<AiProcessingJob> GetAiJobByIdAsync(Guid id, Guid userId)
        {
            return await _warehouseDbContext.AiJobs.Where(i => i.Id == id && i.UserId == userId).FirstAsync();
        }


    }
}