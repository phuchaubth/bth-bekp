using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsentTemplatesAndForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ConsentFormNumbers");

            migrationBuilder.CreateTable(
                name: "ConsentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HospitalSignerRoleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentTemplates_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConsentTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNo = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RetiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentTemplateVersions_ConsentTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ConsentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConsentForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormNo = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DataEncrypted = table.Column<string>(type: "varchar(max)", unicode: false, nullable: false),
                    SignerKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RepresentativeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SnapshotEncrypted = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentForms_ConsentTemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "ConsentTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentForms_ConsentTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ConsentTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentForms_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentForms_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentForms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_CreatedByUserId",
                table: "ConsentForms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_DepartmentId",
                table: "ConsentForms",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_FormNo",
                table: "ConsentForms",
                column: "FormNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_PatientId",
                table: "ConsentForms",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_Status_DepartmentId_CreatedAtUtc",
                table: "ConsentForms",
                columns: new[] { "Status", "DepartmentId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_TemplateId",
                table: "ConsentForms",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_TemplateVersionId",
                table: "ConsentForms",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTemplates_Code",
                table: "ConsentTemplates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTemplates_DepartmentId",
                table: "ConsentTemplates",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentTemplateVersions_TemplateId_VersionNo",
                table: "ConsentTemplateVersions",
                columns: new[] { "TemplateId", "VersionNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentForms");

            migrationBuilder.DropTable(
                name: "ConsentTemplateVersions");

            migrationBuilder.DropTable(
                name: "ConsentTemplates");

            migrationBuilder.DropSequence(
                name: "ConsentFormNumbers");
        }
    }
}
