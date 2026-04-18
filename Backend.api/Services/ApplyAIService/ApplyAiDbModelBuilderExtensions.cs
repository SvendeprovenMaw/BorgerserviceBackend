using Microsoft.EntityFrameworkCore;

namespace Backend.api.Services.ApplyAIService
{
    public static class ApplyAiDbModelBuilderExtensions
    {
        public const string PipelineSchema = "llm_pipeline";

        public static void AddApplyAiPipelineModel(this ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ApplyAiPipelineJob>(builder =>
            {
                builder.ToTable("pipeline_jobs", PipelineSchema);
                builder.HasKey(item => item.Id);
                builder.Property(item => item.StatusMessage).HasColumnType("text");
                builder.Property(item => item.JobPostingReference).HasColumnType("text");
                builder.Property(item => item.SelectedFileIdsJson).HasColumnType("jsonb");
                builder.Property(item => item.CandidateFileSnapshotJson).HasColumnType("jsonb");
                builder.Property(item => item.RequestedArtifactsJson).HasColumnType("jsonb");
                builder.Property(item => item.PreferencesSnapshotJson).HasColumnType("jsonb");
                builder.Property(item => item.DisplayRunName).HasColumnType("text");
                builder.Property(item => item.JobPostingOriginalFileName).HasColumnType("text");
                builder.Property(item => item.JobPostingContentType).HasColumnType("text");
                builder.Property(item => item.CompanyNameOverride).HasColumnType("text");
                builder.Property(item => item.ApplicantAddressHint).HasColumnType("text");
                builder.Property(item => item.RunStoragePrefix).HasColumnType("text");
                builder.Property(item => item.CorrelationId).HasColumnType("text");
                builder.HasIndex(item => new { item.UserId, item.CreatedAtUtc });
                builder.HasMany(item => item.PhaseStates)
                    .WithOne(item => item.Job)
                    .HasForeignKey(item => item.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
                builder.HasMany(item => item.Artifacts)
                    .WithOne(item => item.Job)
                    .HasForeignKey(item => item.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
                builder.HasMany(item => item.Events)
                    .WithOne(item => item.Job)
                    .HasForeignKey(item => item.JobId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ApplyAiPipelinePhaseState>(builder =>
            {
                builder.ToTable("pipeline_phase_states", PipelineSchema);
                builder.HasKey(item => item.Id);
                builder.Property(item => item.StatusMessage).HasColumnType("text");
                builder.Property(item => item.DocumentId).HasColumnType("text");
                builder.Property(item => item.DocumentJson).HasColumnType("jsonb");
                builder.Property(item => item.VerificationJson).HasColumnType("jsonb");
                builder.Property(item => item.GateJson).HasColumnType("jsonb");
                builder.HasIndex(item => new { item.JobId, item.Phase }).IsUnique();
            });

            modelBuilder.Entity<ApplyAiPipelineArtifact>(builder =>
            {
                builder.ToTable("pipeline_artifacts", PipelineSchema);
                builder.HasKey(item => item.Id);
                builder.Property(item => item.RelativePath).HasColumnType("text");
                builder.Property(item => item.StorageKey).HasColumnType("text");
                builder.Property(item => item.DisplayName).HasColumnType("text");
                builder.Property(item => item.MediaType).HasColumnType("text");
                builder.HasIndex(item => new { item.JobId, item.Phase });
            });

            modelBuilder.Entity<ApplyAiPipelineEvent>(builder =>
            {
                builder.ToTable("pipeline_events", PipelineSchema);
                builder.HasKey(item => item.Id);
                builder.Property(item => item.EventId).HasColumnType("text");
                builder.Property(item => item.Message).HasColumnType("text");
                builder.HasIndex(item => new { item.JobId, item.OccurredAtUtc });
            });
        }
    }
}