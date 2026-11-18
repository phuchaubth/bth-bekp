using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Patients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_CreatedByUserId",
                table: "Patients",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Users_CreatedByUserId",
                table: "Patients",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Users_CreatedByUserId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_CreatedByUserId",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Patients");
        }
    }
}
