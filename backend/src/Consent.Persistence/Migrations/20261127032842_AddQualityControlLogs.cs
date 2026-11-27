using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQualityControlLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QcLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Level = table.Column<int>(type: "int", nullable: true),
                    RangeLow = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    RangeHigh = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcLots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QcMeters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcMeters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcMeters_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConfirmedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcBooks_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcBooks_QcMeters_MeterId",
                        column: x => x.MeterId,
                        principalTable: "QcMeters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcDayReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReturnedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReturnedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcDayReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcDayReviews_QcBooks_BookId",
                        column: x => x.BookId,
                        principalTable: "QcBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StripLotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StripLotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StripExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StripOpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Control1LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Control1Level = table.Column<int>(type: "int", nullable: false),
                    Control1LotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Control1ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Control1OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Control1Low = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control1High = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control1Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Control1Value = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control1RepeatValue = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    Control2LotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Control2Level = table.Column<int>(type: "int", nullable: false),
                    Control2LotNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Control2ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Control2OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Control2Low = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control2High = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control2Unit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Control2Value = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: false),
                    Control2RepeatValue = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FollowUpByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FollowUpAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PerformedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReplacesRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReplacedByRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcRuns_QcBooks_BookId",
                        column: x => x.BookId,
                        principalTable: "QcBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcRuns_QcLots_Control1LotId",
                        column: x => x.Control1LotId,
                        principalTable: "QcLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcRuns_QcLots_Control2LotId",
                        column: x => x.Control2LotId,
                        principalTable: "QcLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcRuns_QcLots_StripLotId",
                        column: x => x.StripLotId,
                        principalTable: "QcLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QcBooks_DepartmentId",
                table: "QcBooks",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QcBooks_MeterId_DepartmentId_Year_Month",
                table: "QcBooks",
                columns: new[] { "MeterId", "DepartmentId", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcBooks_Year_Month",
                table: "QcBooks",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_QcDayReviews_BookId_Date",
                table: "QcDayReviews",
                columns: new[] { "BookId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_QcLots_Kind_LotNumber_Level",
                table: "QcLots",
                columns: new[] { "Kind", "LotNumber", "Level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcMeters_DepartmentId",
                table: "QcMeters",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_QcMeters_SerialNumber",
                table: "QcMeters",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcRuns_BookId_RunDate",
                table: "QcRuns",
                columns: new[] { "BookId", "RunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_QcRuns_Control1LotId",
                table: "QcRuns",
                column: "Control1LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QcRuns_Control2LotId",
                table: "QcRuns",
                column: "Control2LotId");

            migrationBuilder.CreateIndex(
                name: "IX_QcRuns_StripLotId",
                table: "QcRuns",
                column: "StripLotId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QcDayReviews");

            migrationBuilder.DropTable(
                name: "QcRuns");

            migrationBuilder.DropTable(
                name: "QcBooks");

            migrationBuilder.DropTable(
                name: "QcLots");

            migrationBuilder.DropTable(
                name: "QcMeters");
        }
    }
}
