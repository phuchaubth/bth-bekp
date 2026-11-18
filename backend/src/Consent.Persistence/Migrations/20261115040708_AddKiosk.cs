using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKiosk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KioskLookupFailures",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KioskLookupLockedUntilUtc",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KioskIdentity",
                table: "ConsentForms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KioskLookupFailures",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KioskLookupLockedUntilUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "KioskIdentity",
                table: "ConsentForms");
        }
    }
}
