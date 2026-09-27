using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppearance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThemeId",
                table: "Users",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseBrandAccent",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "UseBrandAccent",
                table: "Users");
        }
    }
}
