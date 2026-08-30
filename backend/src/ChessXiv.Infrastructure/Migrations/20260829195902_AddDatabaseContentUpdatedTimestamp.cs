using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseContentUpdatedTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ContentUpdatedAtUtc",
                table: "UserDatabases",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Existing databases have no modification history, so their creation date is the
            // only honest answer - better than showing every one of them as year 1.
            migrationBuilder.Sql("""
                UPDATE "UserDatabases" SET "ContentUpdatedAtUtc" = "CreatedAtUtc";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentUpdatedAtUtc",
                table: "UserDatabases");
        }
    }
}
