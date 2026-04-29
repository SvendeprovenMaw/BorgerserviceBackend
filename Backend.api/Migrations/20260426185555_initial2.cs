using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class initial2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_S3File_AiJobs_AiProcessingJobId",
                table: "S3File");

            migrationBuilder.DropForeignKey(
                name: "FK_S3File_Profiles_ProfileId",
                table: "S3File");

            migrationBuilder.DropTable(
                name: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_S3File_AiProcessingJobId",
                table: "S3File");

            migrationBuilder.DropIndex(
                name: "IX_S3File_ProfileId",
                table: "S3File");

            migrationBuilder.DropColumn(
                name: "AiProcessingJobId",
                table: "S3File");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "S3File");

            migrationBuilder.AddColumn<string>(
                name: "Application",
                table: "AiJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JobRequirements",
                table: "AiJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Matches",
                table: "AiJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserCompetences",
                table: "AiJobs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "AiJobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "AiJobProcessedFiles",
                columns: table => new
                {
                    AiJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    S3FileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiJobProcessedFiles", x => new { x.AiJobId, x.S3FileId });
                    table.ForeignKey(
                        name: "FK_AiJobProcessedFiles_AiJobs_AiJobId",
                        column: x => x.AiJobId,
                        principalTable: "AiJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiJobProcessedFiles_S3File_S3FileId",
                        column: x => x.S3FileId,
                        principalTable: "S3File",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiJobs_UserId",
                table: "AiJobs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiJobProcessedFiles_S3FileId",
                table: "AiJobProcessedFiles",
                column: "S3FileId");

            migrationBuilder.AddForeignKey(
                name: "FK_AiJobs_Users_UserId",
                table: "AiJobs",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiJobs_Users_UserId",
                table: "AiJobs");

            migrationBuilder.DropTable(
                name: "AiJobProcessedFiles");

            migrationBuilder.DropIndex(
                name: "IX_AiJobs_UserId",
                table: "AiJobs");

            migrationBuilder.DropColumn(
                name: "Application",
                table: "AiJobs");

            migrationBuilder.DropColumn(
                name: "JobRequirements",
                table: "AiJobs");

            migrationBuilder.DropColumn(
                name: "Matches",
                table: "AiJobs");

            migrationBuilder.DropColumn(
                name: "UserCompetences",
                table: "AiJobs");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "AiJobs");

            migrationBuilder.AddColumn<Guid>(
                name: "AiProcessingJobId",
                table: "S3File",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProfileId",
                table: "S3File",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentCvId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_S3File_CurrentCvId",
                        column: x => x.CurrentCvId,
                        principalTable: "S3File",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Profiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_S3File_AiProcessingJobId",
                table: "S3File",
                column: "AiProcessingJobId");

            migrationBuilder.CreateIndex(
                name: "IX_S3File_ProfileId",
                table: "S3File",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_CurrentCvId",
                table: "Profiles",
                column: "CurrentCvId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_S3File_AiJobs_AiProcessingJobId",
                table: "S3File",
                column: "AiProcessingJobId",
                principalTable: "AiJobs",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_S3File_Profiles_ProfileId",
                table: "S3File",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id");
        }
    }
}
