using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentFormEncounter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EncounterNo",
                table: "ConsentForms",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncounterPlace",
                table: "ConsentForms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EncounterReceivedAt",
                table: "ConsentForms",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncounterReceivedBy",
                table: "ConsentForms",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_PatientId_EncounterNo",
                table: "ConsentForms",
                columns: new[] { "PatientId", "EncounterNo" },
                filter: "[EncounterNo] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_PatientId_EncounterNo",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "EncounterNo",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "EncounterPlace",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "EncounterReceivedAt",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "EncounterReceivedBy",
                table: "ConsentForms");
        }
    }
}
