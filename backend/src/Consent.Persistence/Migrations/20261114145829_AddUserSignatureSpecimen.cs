using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSignatureSpecimen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureSpecimenEncrypted",
                table: "Users",
                type: "varchar(max)",
                unicode: false,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignatureSpecimenUpdatedAtUtc",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SignatureSpecimenEncrypted",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SignatureSpecimenUpdatedAtUtc",
                table: "Users");
        }
    }
}
