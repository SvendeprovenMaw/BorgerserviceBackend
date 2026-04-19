using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class AddSentApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SentApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PipelineJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Company = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    ContactEmail = table.Column<string>(type: "text", nullable: false),
                    JobPosting = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApplicantName = table.Column<string>(type: "text", nullable: false),
                    SubjectLine = table.Column<string>(type: "text", nullable: false),
                    FinalText = table.Column<string>(type: "text", nullable: false),
                    TemplateSnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    SectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CompanyContextJson = table.Column<string>(type: "jsonb", nullable: false),
                    GeneratedArtifactsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SentApplications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SentApplications_PipelineJobId",
                table: "SentApplications",
                column: "PipelineJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SentApplications_UserId_SentAtUtc",
                table: "SentApplications",
                columns: new[] { "UserId", "SentAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SentApplications");
        }
    }
}
