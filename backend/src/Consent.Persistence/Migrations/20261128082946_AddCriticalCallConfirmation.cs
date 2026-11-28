using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCriticalCallConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReceiverName",
                table: "CriticalNotifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CallRecordedAtUtc",
                table: "CriticalNotifications",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CallRecordedByName",
                table: "CriticalNotifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CallRecordedByUserId",
                table: "CriticalNotifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CalledAtUtc",
                table: "CriticalNotifications",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CallRecordedAtUtc",
                table: "CriticalNotifications");

            migrationBuilder.DropColumn(
                name: "CallRecordedByName",
                table: "CriticalNotifications");

            migrationBuilder.DropColumn(
                name: "CallRecordedByUserId",
                table: "CriticalNotifications");

            migrationBuilder.DropColumn(
                name: "CalledAtUtc",
                table: "CriticalNotifications");

            migrationBuilder.AlterColumn<string>(
                name: "ReceiverName",
                table: "CriticalNotifications",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
