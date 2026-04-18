using Backend.api.Entities;
using Backend.api.Services.ApplyAIService;
using Microsoft.EntityFrameworkCore;

namespace Backend.api.Database
{
    public class ApplyAIDbContext : DbContext
    {
        public ApplyAIDbContext(DbContextOptions<ApplyAIDbContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<S3File>().ToTable("S3File");
            modelBuilder.Entity<Term>().ToTable("Terms");
            modelBuilder.AddApplyAiPipelineModel();

            modelBuilder.Entity<Profile>()
                .HasIndex(profile => profile.UserId)
                .IsUnique();

            modelBuilder.Entity<Profile>()
                .HasIndex(profile => profile.ApplicantId)
                .IsUnique();

            modelBuilder.Entity<Profile>()
                .HasOne(profile => profile.CurrentCv)
                .WithMany()
                .HasForeignKey(profile => profile.CurrentCvId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Profile>()
                .Property(profile => profile.ProfileEnhancementJson)
                .HasColumnType("jsonb");

            modelBuilder.Entity<Profile>()
                .Property(profile => profile.PreferencesJson)
                .HasColumnType("jsonb");

            //makes sure theres only one consent pr file            
            modelBuilder.Entity<Consent>()
                .HasIndex(c => new { c.FileId })
                .IsUnique();

            //makes sure there can only be 1 term that has an active bool
            modelBuilder.Entity<Term>()
                .HasIndex(t => t.Active)
                .IsUnique()
                .HasFilter("\"Active\" = TRUE");
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Profile> Profiles { get; set; }
        public DbSet<AiProcessingJob> AiJobs { get; set; }
        public DbSet<AiDraft> AiDrafts { get; set; }
        public DbSet<S3File> S3Files { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<Term> Term { get; set; }
        public DbSet<Consent> Consents { get; set; }
        public DbSet<ApplyAiPipelineJob> ApplyAiPipelineJobs { get; set; }
        public DbSet<ApplyAiPipelinePhaseState> ApplyAiPipelinePhaseStates { get; set; }
        public DbSet<ApplyAiPipelineArtifact> ApplyAiPipelineArtifacts { get; set; }
        public DbSet<ApplyAiPipelineEvent> ApplyAiPipelineEvents { get; set; }

    }
}