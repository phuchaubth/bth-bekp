using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentSignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Decision",
                table: "ConsentForms",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesFormId",
                table: "ConsentForms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfKey",
                table: "ConsentForms",
                type: "varchar(200)",
                unicode: false,
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignedPdfSha256",
                table: "ConsentForms",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SignedPdfSize",
                table: "ConsentForms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SigningSessionId",
                table: "ConsentForms",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConsentSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SignerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SignerTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImagePngEncrypted = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    Device = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentSignatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentSignatures_ConsentForms_FormId",
                        column: x => x.FormId,
                        principalTable: "ConsentForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConsentSignatures_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_ReplacesFormId",
                table: "ConsentForms",
                column: "ReplacesFormId",
                unique: true,
                filter: "[ReplacesFormId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentSignatures_FormId",
                table: "ConsentSignatures",
                column: "FormId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentSignatures_UserId",
                table: "ConsentSignatures",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsentForms_ConsentForms_ReplacesFormId",
                table: "ConsentForms",
                column: "ReplacesFormId",
                principalTable: "ConsentForms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConsentForms_ConsentForms_ReplacesFormId",
                table: "ConsentForms");

            migrationBuilder.DropTable(
                name: "ConsentSignatures");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_ReplacesFormId",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "Decision",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "ReplacesFormId",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "SignedPdfKey",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "SignedPdfSha256",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "SignedPdfSize",
                table: "ConsentForms");

            migrationBuilder.DropColumn(
                name: "SigningSessionId",
                table: "ConsentForms");
        }
    }
}
