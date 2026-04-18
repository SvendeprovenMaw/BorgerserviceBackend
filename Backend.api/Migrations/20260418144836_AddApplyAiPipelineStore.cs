using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplyAiPipelineStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "llm_pipeline");

            migrationBuilder.CreateTable(
                name: "pipeline_jobs",
                schema: "llm_pipeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkflowMode = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentPhase = table.Column<int>(type: "integer", nullable: true),
                    CurrentActivity = table.Column<int>(type: "integer", nullable: false),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<string>(type: "text", nullable: true),
                    JobPostingSourceType = table.Column<int>(type: "integer", nullable: false),
                    JobPostingReference = table.Column<string>(type: "text", nullable: false),
                    JobPostingOriginalFileName = table.Column<string>(type: "text", nullable: true),
                    JobPostingContentType = table.Column<string>(type: "text", nullable: true),
                    IncludeCurrentCv = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeProfileRelevantDocuments = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeAllConsentedFiles = table.Column<bool>(type: "boolean", nullable: false),
                    SelectedFileIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CandidateFileSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    RequestedArtifactsJson = table.Column<string>(type: "jsonb", nullable: false),
                    PreferencesSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    CompanyNameOverride = table.Column<string>(type: "text", nullable: true),
                    ApplicantAddressHint = table.Column<string>(type: "text", nullable: true),
                    RunStoragePrefix = table.Column<string>(type: "text", nullable: true),
                    DisplayRunName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_jobs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_artifacts",
                schema: "llm_pipeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: true),
                    ArtifactKind = table.Column<int>(type: "integer", nullable: false),
                    RelativePath = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    MediaType = table.Column<string>(type: "text", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_artifacts_pipeline_jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "llm_pipeline",
                        principalTable: "pipeline_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_events",
                schema: "llm_pipeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    JobStatus = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: true),
                    Activity = table.Column<int>(type: "integer", nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_events_pipeline_jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "llm_pipeline",
                        principalTable: "pipeline_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_phase_states",
                schema: "llm_pipeline",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentActivity = table.Column<int>(type: "integer", nullable: true),
                    StatusMessage = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    RepairAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    WarningCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    ApprovalRequired = table.Column<bool>(type: "boolean", nullable: false),
                    ApprovedForDownstream = table.Column<bool>(type: "boolean", nullable: false),
                    HasUnverifiedEdits = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DocumentId = table.Column<string>(type: "text", nullable: true),
                    DocumentJson = table.Column<string>(type: "jsonb", nullable: true),
                    VerificationJson = table.Column<string>(type: "jsonb", nullable: true),
                    GateJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_phase_states", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pipeline_phase_states_pipeline_jobs_JobId",
                        column: x => x.JobId,
                        principalSchema: "llm_pipeline",
                        principalTable: "pipeline_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_artifacts_JobId_Phase",
                schema: "llm_pipeline",
                table: "pipeline_artifacts",
                columns: new[] { "JobId", "Phase" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_events_JobId_OccurredAtUtc",
                schema: "llm_pipeline",
                table: "pipeline_events",
                columns: new[] { "JobId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_jobs_UserId_CreatedAtUtc",
                schema: "llm_pipeline",
                table: "pipeline_jobs",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_phase_states_JobId_Phase",
                schema: "llm_pipeline",
                table: "pipeline_phase_states",
                columns: new[] { "JobId", "Phase" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pipeline_artifacts",
                schema: "llm_pipeline");

            migrationBuilder.DropTable(
                name: "pipeline_events",
                schema: "llm_pipeline");

            migrationBuilder.DropTable(
                name: "pipeline_phase_states",
                schema: "llm_pipeline");

            migrationBuilder.DropTable(
                name: "pipeline_jobs",
                schema: "llm_pipeline");
        }
    }
}
