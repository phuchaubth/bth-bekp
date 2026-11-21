using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNursingReportDelegations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DelegationId",
                table: "NursingDepartmentStatistics",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedDelegationId",
                table: "NursingDailyReports",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NursingReportDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegatorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NursingReportDelegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NursingReportDelegations_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NursingDepartmentStatistics_DelegationId",
                table: "NursingDepartmentStatistics",
                column: "DelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_NursingDailyReports_SubmittedDelegationId",
                table: "NursingDailyReports",
                column: "SubmittedDelegationId");

            migrationBuilder.CreateIndex(
                name: "IX_NursingReportDelegations_DelegateUserId",
                table: "NursingReportDelegations",
                column: "DelegateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NursingReportDelegations_DelegatorUserId",
                table: "NursingReportDelegations",
                column: "DelegatorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NursingReportDelegations_DepartmentId",
                table: "NursingReportDelegations",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_NursingReportDelegations_StartDate_EndDate",
                table: "NursingReportDelegations",
                columns: new[] { "StartDate", "EndDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_NursingDailyReports_NursingReportDelegations_SubmittedDelegationId",
                table: "NursingDailyReports",
                column: "SubmittedDelegationId",
                principalTable: "NursingReportDelegations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_NursingDepartmentStatistics_NursingReportDelegations_DelegationId",
                table: "NursingDepartmentStatistics",
                column: "DelegationId",
                principalTable: "NursingReportDelegations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NursingDailyReports_NursingReportDelegations_SubmittedDelegationId",
                table: "NursingDailyReports");

            migrationBuilder.DropForeignKey(
                name: "FK_NursingDepartmentStatistics_NursingReportDelegations_DelegationId",
                table: "NursingDepartmentStatistics");

            migrationBuilder.DropTable(
                name: "NursingReportDelegations");

            migrationBuilder.DropIndex(
                name: "IX_NursingDepartmentStatistics_DelegationId",
                table: "NursingDepartmentStatistics");

            migrationBuilder.DropIndex(
                name: "IX_NursingDailyReports_SubmittedDelegationId",
                table: "NursingDailyReports");

            migrationBuilder.DropColumn(
                name: "DelegationId",
                table: "NursingDepartmentStatistics");

            migrationBuilder.DropColumn(
                name: "SubmittedDelegationId",
                table: "NursingDailyReports");
        }
    }
}
