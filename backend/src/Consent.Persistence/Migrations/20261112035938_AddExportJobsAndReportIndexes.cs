using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExportJobsAndReportIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_CreatedByUserId",
                table: "ConsentForms");

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Format = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FiltersJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Columns = table.Column<string>(type: "varchar(2000)", unicode: false, maxLength: 2000, nullable: false),
                    IncludesContent = table.Column<bool>(type: "bit", nullable: false),
                    IncludesSensitiveData = table.Column<bool>(type: "bit", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    DurationMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExportJobs_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_CompletedAtUtc",
                table: "ConsentForms",
                column: "CompletedAtUtc",
                filter: "[CompletedAtUtc] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "DepartmentId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_CreatedAtUtc",
                table: "ConsentForms",
                column: "CreatedAtUtc")
                .Annotation("SqlServer:Include", new[] { "DepartmentId", "TemplateId", "Status", "Decision", "SignerKind", "CreatedByUserId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_CreatedByUserId_Status",
                table: "ConsentForms",
                columns: new[] { "CreatedByUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_RevokedAtUtc",
                table: "ConsentForms",
                column: "RevokedAtUtc",
                filter: "[RevokedAtUtc] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "DepartmentId", "CreatedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_RequestedAtUtc",
                table: "ExportJobs",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_RequestedByUserId_RequestedAtUtc",
                table: "ExportJobs",
                columns: new[] { "RequestedByUserId", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_CompletedAtUtc",
                table: "ConsentForms");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_CreatedAtUtc",
                table: "ConsentForms");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_CreatedByUserId_Status",
                table: "ConsentForms");

            migrationBuilder.DropIndex(
                name: "IX_ConsentForms_RevokedAtUtc",
                table: "ConsentForms");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentForms_CreatedByUserId",
                table: "ConsentForms",
                column: "CreatedByUserId");
        }
    }
}
