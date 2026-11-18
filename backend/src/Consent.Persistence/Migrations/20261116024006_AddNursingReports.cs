using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNursingReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NursingDailyReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FiguresJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NotesEncrypted = table.Column<string>(type: "varchar(max)", unicode: false, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReopenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReopenReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NursingDailyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NursingDailyReports_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NursingDailyStatistics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NursingDailyStatistics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NursingReportDepartments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NursingReportDepartments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NursingReportDepartments_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NursingDailyReports_DepartmentId_ReportDate",
                table: "NursingDailyReports",
                columns: new[] { "DepartmentId", "ReportDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NursingDailyReports_ReportDate",
                table: "NursingDailyReports",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_NursingDailyStatistics_ReportDate",
                table: "NursingDailyStatistics",
                column: "ReportDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NursingReportDepartments_DepartmentId",
                table: "NursingReportDepartments",
                column: "DepartmentId",
                unique: true);

            // Quyết định 31: vai trò Điều dưỡng đã có trên CSDL được thêm quyền lập và gửi báo cáo điều dưỡng (chỉ một lần, khi
            // nâng cấp). CSDL mới chưa có vai trò nào: phần khởi tạo tạo vai trò với quyền mặc định, đã gồm quyền này.
            migrationBuilder.Sql(
                """
                INSERT INTO [RolePermissions] ([RoleId], [PermissionCode])
                SELECT r.[Id], 'nursingreports.submit'
                FROM [Roles] r
                WHERE r.[Code] = 'NURSE'
                  AND NOT EXISTS (SELECT 1 FROM [RolePermissions] p WHERE p.[RoleId] = r.[Id] AND p.[PermissionCode] = 'nursingreports.submit');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [RolePermissions] WHERE [PermissionCode] LIKE 'nursingreports.%';");

            migrationBuilder.DropTable(
                name: "NursingDailyReports");

            migrationBuilder.DropTable(
                name: "NursingDailyStatistics");

            migrationBuilder.DropTable(
                name: "NursingReportDepartments");
        }
    }
}
