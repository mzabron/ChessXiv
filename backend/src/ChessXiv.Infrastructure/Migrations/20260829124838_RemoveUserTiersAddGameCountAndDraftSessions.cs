using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUserTiersAddGameCountAndDraftSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UserTier",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "GameCount",
                table: "UserDatabases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Backfill the denormalised count so existing databases do not render as empty.
            migrationBuilder.Sql("""
                UPDATE "UserDatabases" d
                SET "GameCount" = (
                    SELECT count(*) FROM "UserDatabaseGames" l WHERE l."UserDatabaseId" = d."Id"
                );
                """);

            migrationBuilder.CreateTable(
                name: "StagingDraftSessions",
                columns: table => new
                {
                    OwnerUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAccessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingDraftSessions", x => x.OwnerUserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StagingDraftSessions_LastAccessedAtUtc",
                table: "StagingDraftSessions",
                column: "LastAccessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StagingDraftSessions");

            migrationBuilder.DropColumn(
                name: "GameCount",
                table: "UserDatabases");

            migrationBuilder.AddColumn<string>(
                name: "UserTier",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }
    }
}
