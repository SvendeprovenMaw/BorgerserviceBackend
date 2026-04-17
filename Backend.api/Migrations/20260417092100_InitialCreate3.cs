using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate3 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_S3File_UserId",
                table: "S3File",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_S3File_Users_UserId",
                table: "S3File",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_S3File_Users_UserId",
                table: "S3File");

            migrationBuilder.DropIndex(
                name: "IX_S3File_UserId",
                table: "S3File");
        }
    }
}
