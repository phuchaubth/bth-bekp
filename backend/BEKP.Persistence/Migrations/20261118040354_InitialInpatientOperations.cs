using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BEKP.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialInpatientOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "bekp");

            migrationBuilder.CreateTable(
                name: "BloodInventory",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Product = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AboGroup = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    UnitsAvailable = table.Column<int>(type: "int", nullable: false),
                    MinimumUnits = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodInventory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Group = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    IsSterileUnit = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OperationalEvents",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepartmentCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    StayCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Value = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationalEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Beds",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    RoomLabel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsExtra = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Beds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Beds_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "bekp",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessTimings",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Standard = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    Subject = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessTimings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessTimings_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "bekp",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InpatientStays",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BedId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DischargedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CareLevel = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    PrincipalDiagnosisCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    DiseaseGroup = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    IsNeutropenic = table.Column<bool>(type: "bit", nullable: false),
                    IsTransfusing = table.Column<bool>(type: "bit", nullable: false),
                    HasChemotherapyToday = table.Column<bool>(type: "bit", nullable: false),
                    ExpectedDischargeDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InpatientStays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InpatientStays_Beds_BedId",
                        column: x => x.BedId,
                        principalSchema: "bekp",
                        principalTable: "Beds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InpatientStays_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "bekp",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BloodRequests",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Product = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    AboGroup = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    RhDPositive = table.Column<bool>(type: "bit", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false),
                    Urgency = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    Modifiers = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StatusChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TransfusionStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BloodRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BloodRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "bekp",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BloodRequests_InpatientStays_StayId",
                        column: x => x.StayId,
                        principalSchema: "bekp",
                        principalTable: "InpatientStays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FebrileNeutropeniaEpisodes",
                schema: "bekp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FeverDetectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TemperatureCelsius = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    AntibioticStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FebrileNeutropeniaEpisodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FebrileNeutropeniaEpisodes_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalSchema: "bekp",
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FebrileNeutropeniaEpisodes_InpatientStays_StayId",
                        column: x => x.StayId,
                        principalSchema: "bekp",
                        principalTable: "InpatientStays",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Beds_DepartmentId_Code",
                schema: "bekp",
                table: "Beds",
                columns: new[] { "DepartmentId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BloodInventory_Product_AboGroup",
                schema: "bekp",
                table: "BloodInventory",
                columns: new[] { "Product", "AboGroup" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_DepartmentId_Status",
                schema: "bekp",
                table: "BloodRequests",
                columns: new[] { "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_IssuedAtUtc",
                schema: "bekp",
                table: "BloodRequests",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BloodRequests_StayId",
                schema: "bekp",
                table: "BloodRequests",
                column: "StayId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Code",
                schema: "bekp",
                table: "Departments",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FebrileNeutropeniaEpisodes_DepartmentId_AntibioticStartedAtUtc",
                schema: "bekp",
                table: "FebrileNeutropeniaEpisodes",
                columns: new[] { "DepartmentId", "AntibioticStartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FebrileNeutropeniaEpisodes_StayId",
                schema: "bekp",
                table: "FebrileNeutropeniaEpisodes",
                column: "StayId");

            migrationBuilder.CreateIndex(
                name: "IX_InpatientStays_AdmittedAtUtc",
                schema: "bekp",
                table: "InpatientStays",
                column: "AdmittedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InpatientStays_BedId",
                schema: "bekp",
                table: "InpatientStays",
                column: "BedId");

            migrationBuilder.CreateIndex(
                name: "IX_InpatientStays_Code",
                schema: "bekp",
                table: "InpatientStays",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InpatientStays_DepartmentId_DischargedAtUtc",
                schema: "bekp",
                table: "InpatientStays",
                columns: new[] { "DepartmentId", "DischargedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_DepartmentId_OccurredAtUtc",
                schema: "bekp",
                table: "OperationalEvents",
                columns: new[] { "DepartmentId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OperationalEvents_OccurredAtUtc",
                schema: "bekp",
                table: "OperationalEvents",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTimings_CompletedAtUtc_DepartmentId",
                schema: "bekp",
                table: "ProcessTimings",
                columns: new[] { "CompletedAtUtc", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessTimings_DepartmentId",
                schema: "bekp",
                table: "ProcessTimings",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BloodInventory",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "BloodRequests",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "FebrileNeutropeniaEpisodes",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "OperationalEvents",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "ProcessTimings",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "InpatientStays",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "Beds",
                schema: "bekp");

            migrationBuilder.DropTable(
                name: "Departments",
                schema: "bekp");
        }
    }
}
