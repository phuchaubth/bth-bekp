using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentSignatureStepKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StepKey",
                table: "ConsentSignatures",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentSignatures_FormId_StepKey",
                table: "ConsentSignatures",
                columns: new[] { "FormId", "StepKey" },
                unique: true,
                filter: "[StepKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsentSignatures_FormId_StepKey",
                table: "ConsentSignatures");

            migrationBuilder.DropColumn(
                name: "StepKey",
                table: "ConsentSignatures");
        }
    }
}
