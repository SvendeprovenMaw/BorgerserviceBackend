using Backend.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Database
{
    public class WarehouseDbContext : DbContext
    {
        public WarehouseDbContext() { }
        public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<AiProcessingJob> AiJobs { get; set; }
        public DbSet<AiDraft> AiDrafts { get; set; }
        public DbSet<S3File> S3Files { get; set; }


    }
}