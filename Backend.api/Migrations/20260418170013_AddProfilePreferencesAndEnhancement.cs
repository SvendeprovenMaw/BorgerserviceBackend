using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePreferencesAndEnhancement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profiles_S3File_CurrentCvId",
                table: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles");

            migrationBuilder.AddColumn<string>(
                name: "ApplicantId",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Municipality",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferencesJson",
                table: "Profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileEnhancementJson",
                table: "Profiles",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortBio",
                table: "Profiles",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""Profiles"" AS p
                SET ""ApplicantId"" = COALESCE(NULLIF(u.""Username"", ''), u.""Id""::text),
                    ""FullName"" = COALESCE(NULLIF(u.""Username"", ''), u.""Email""),
                    ""Municipality"" = COALESCE(""Municipality"", ''),
                    ""PhoneNumber"" = COALESCE(""PhoneNumber"", ''),
                    ""PreferencesJson"" = COALESCE(""PreferencesJson"", '{}'::jsonb),
                    ""ProfileEnhancementJson"" = COALESCE(""ProfileEnhancementJson"", '{}'::jsonb),
                    ""ShortBio"" = COALESCE(""ShortBio"", '')
                FROM ""Users"" AS u
                WHERE p.""UserId"" = u.""Id"";
            ");

            migrationBuilder.AlterColumn<string>(
                name: "ApplicantId",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Municipality",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PhoneNumber",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PreferencesJson",
                table: "Profiles",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ProfileEnhancementJson",
                table: "Profiles",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShortBio",
                table: "Profiles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_ApplicantId",
                table: "Profiles",
                column: "ApplicantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Profiles_S3File_CurrentCvId",
                table: "Profiles",
                column: "CurrentCvId",
                principalTable: "S3File",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Profiles_S3File_CurrentCvId",
                table: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_ApplicantId",
                table: "Profiles");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ApplicantId",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Municipality",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "PreferencesJson",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ProfileEnhancementJson",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ShortBio",
                table: "Profiles");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_UserId",
                table: "Profiles",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Profiles_S3File_CurrentCvId",
                table: "Profiles",
                column: "CurrentCvId",
                principalTable: "S3File",
                principalColumn: "Id");
        }
    }
}
