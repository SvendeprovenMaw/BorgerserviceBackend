using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class AddApplyAiArtifactStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                schema: "llm_pipeline",
                table: "pipeline_artifacts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageKey",
                schema: "llm_pipeline",
                table: "pipeline_artifacts");
        }
    }
}
