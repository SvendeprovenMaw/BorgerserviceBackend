using Backend.api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Database
{
    public class ApplyAiDbContext : DbContext
    {
        public ApplyAiDbContext(DbContextOptions<ApplyAiDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<S3File>().ToTable("S3File");
            modelBuilder.Entity<Term>().ToTable("Terms");

            //makes sure theres only one consent pr file            
            modelBuilder.Entity<Consent>()
                .HasIndex(c => new { c.FileId })
                .IsUnique();

            //makes sure there can only be 1 term that has an active bool
            modelBuilder.Entity<Term>()
                .HasIndex(t => t.Active)
                .IsUnique()
                .HasFilter("\"Active\" = TRUE");

            modelBuilder.Entity<AiProcessingJob>()
                .HasMany(j => j.ProcessedFiles)
                .WithMany() // No navigation property on S3File back to AiProcessingJob
                .UsingEntity<Dictionary<string, object>>(
                    "AiJobProcessedFiles", // Name of the bridge table
                    j => j.HasOne<S3File>().WithMany().HasForeignKey("S3FileId"),
                    j => j.HasOne<AiProcessingJob>().WithMany().HasForeignKey("AiJobId")
                );

            // If ResultFile is a one-to-one or one-to-many reference
            modelBuilder.Entity<AiProcessingJob>()
                .HasOne(j => j.ResultFile)
                .WithMany() // S3File can be a result for multiple jobs, or just leave empty
                .HasForeignKey("ResultFileId");

        }

        public DbSet<User> Users { get; set; }
        public DbSet<AiProcessingJob> AiJobs { get; set; }
        public DbSet<AiDraft> AiDrafts { get; set; }
        public DbSet<S3File> S3Files { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Term> Term { get; set; }
        public DbSet<Consent> Consents { get; set; }

    }
}