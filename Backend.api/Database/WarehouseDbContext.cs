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
            modelBuilder.Entity<S3File>().ToTable("S3File");
            modelBuilder.Entity<Term>().ToTable("Terms");
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<AiProcessingJob> AiJobs { get; set; }
        public DbSet<AiDraft> AiDrafts { get; set; }
        public DbSet<S3File> S3Files { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Term> Term { get; set; }

    }
}