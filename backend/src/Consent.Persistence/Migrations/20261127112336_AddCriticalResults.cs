using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "CriticalResultNumbers");

            migrationBuilder.CreateTable(
                name: "CriticalTests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AdultRange = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChildRange = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Section = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Logbook = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TextResult = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalTests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CriticalResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SendingDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PatientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PatientDateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    PatientGender = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PatientAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PatientSource = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TestAdultRange = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TestChildRange = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Logbook = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TextResult = table.Column<bool>(type: "bit", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResultAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SampleId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Urgent = table.Column<bool>(type: "bit", nullable: false),
                    NotifyDeadlineMinutes = table.Column<int>(type: "int", nullable: false),
                    ActionDeadlineMinutes = table.Column<int>(type: "int", nullable: false),
                    LateEntryMinutes = table.Column<int>(type: "int", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReceivingDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NotifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcknowledgedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActionByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActionByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ActionAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ActionSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedInMedicalRecord = table.Column<bool>(type: "bit", nullable: false),
                    ActionRecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CancelledByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceivingDepartmentInformed = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CriticalResults_CriticalTests_TestId",
                        column: x => x.TestId,
                        principalTable: "CriticalTests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CriticalResults_Departments_ReceivingDepartmentId",
                        column: x => x.ReceivingDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CriticalResults_Departments_SendingDepartmentId",
                        column: x => x.SendingDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CriticalNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptNo = table.Column<int>(type: "int", nullable: false),
                    ReceivingDepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NotifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceiverName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IncorrectReadBacks = table.Column<int>(type: "int", nullable: false),
                    RepeatedTwice = table.Column<bool>(type: "bit", nullable: false),
                    NotifiedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotifiedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RejectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RejectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CriticalNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CriticalNotifications_CriticalResults_ResultId",
                        column: x => x.ResultId,
                        principalTable: "CriticalResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CriticalNotifications_Departments_ReceivingDepartmentId",
                        column: x => x.ReceivingDepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalNotifications_ReceivingDepartmentId",
                table: "CriticalNotifications",
                column: "ReceivingDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CriticalNotifications_ResultId_AttemptNo",
                table: "CriticalNotifications",
                columns: new[] { "ResultId", "AttemptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_Number",
                table: "CriticalResults",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_ReceivingDepartmentId_Status_UpdatedAtUtc",
                table: "CriticalResults",
                columns: new[] { "ReceivingDepartmentId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_ResultAtUtc",
                table: "CriticalResults",
                column: "ResultAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_SendingDepartmentId_Status_UpdatedAtUtc",
                table: "CriticalResults",
                columns: new[] { "SendingDepartmentId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CriticalResults_TestId",
                table: "CriticalResults",
                column: "TestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CriticalNotifications");

            migrationBuilder.DropTable(
                name: "CriticalResults");

            migrationBuilder.DropTable(
                name: "CriticalTests");

            migrationBuilder.DropSequence(
                name: "CriticalResultNumbers");
        }
    }
}
