using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentFormInheritedFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InheritedFromFormId",
                table: "ConsentForms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_InheritedFromFormId",
                table: "ConsentForms",
                column: "InheritedFromFormId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsentForms_ConsentForms_InheritedFromFormId",
                table: "ConsentForms",
                column: "InheritedFromFormId",
                principalTable: "ConsentForms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsentForms_ConsentForms_InheritedFromFormId",
                table: "ConsentForms");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_InheritedFromFormId",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "InheritedFromFormId",
                table: "ConsentForms");
        }
    }
}
