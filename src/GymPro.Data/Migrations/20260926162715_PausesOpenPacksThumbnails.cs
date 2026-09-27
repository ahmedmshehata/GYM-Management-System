using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymPro.Data.Migrations
{
    /// <inheritdoc />
    public partial class PausesOpenPacksThumbnails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "TEXT");

            migrationBuilder.AddColumn<int>(
                name: "PauseDaysAllowed",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PausedDays",
                table: "Subscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PausedFrom",
                table: "Subscriptions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowPause",
                table: "Plans",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxPauseDays",
                table: "Plans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "SessionsExpire",
                table: "Plans",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Thumbnail",
                table: "MemberPhotos",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PauseDaysAllowed",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PausedDays",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "PausedFrom",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "AllowPause",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "MaxPauseDays",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "SessionsExpire",
                table: "Plans");

            migrationBuilder.DropColumn(
                name: "Thumbnail",
                table: "MemberPhotos");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EndDate",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
