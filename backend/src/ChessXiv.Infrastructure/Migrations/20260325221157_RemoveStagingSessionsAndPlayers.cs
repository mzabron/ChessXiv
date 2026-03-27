using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStagingSessionsAndPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Players_BlackPlayerId",
                table: "Games");

            migrationBuilder.DropForeignKey(
                name: "FK_Games_Players_WhitePlayerId",
                table: "Games");

            migrationBuilder.DropForeignKey(
                name: "FK_StagingGames_StagingImportSessions_ImportSessionId",
                table: "StagingGames");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "StagingImportSessions");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_ImportSessionId",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_Black",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_GameHash",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_White",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_Games_BlackPlayerId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_WhitePlayerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BlackPlayerId",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "ImportSessionId",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "WhitePlayerId",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "BlackPlayerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WhitePlayerId",
                table: "Games");

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedFirstName",
                table: "StagingGames",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedFullName",
                table: "StagingGames",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedLastName",
                table: "StagingGames",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "StagingGames",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedFirstName",
                table: "StagingGames",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedFullName",
                table: "StagingGames",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedLastName",
                table: "StagingGames",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedFirstName",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedFullName",
                table: "Games",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BlackNormalizedLastName",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedFirstName",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedFullName",
                table: "Games",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WhiteNormalizedLastName",
                table: "Games",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_Black",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "Black" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_BlackNormalizedFirstName_BlackNorm~",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "BlackNormalizedFirstName", "BlackNormalizedLastName" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_CreatedAtUtc",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_GameHash",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "GameHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_White",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "White" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_WhiteNormalizedFirstName_WhiteNorm~",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "WhiteNormalizedFirstName", "WhiteNormalizedLastName" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_BlackNormalizedFirstName_BlackNormalizedLastName",
                table: "Games",
                columns: new[] { "BlackNormalizedFirstName", "BlackNormalizedLastName" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_WhiteNormalizedFirstName_WhiteNormalizedLastName",
                table: "Games",
                columns: new[] { "WhiteNormalizedFirstName", "WhiteNormalizedLastName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_Black",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_BlackNormalizedFirstName_BlackNorm~",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_CreatedAtUtc",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_GameHash",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_White",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_WhiteNormalizedFirstName_WhiteNorm~",
                table: "StagingGames");

            migrationBuilder.DropIndex(
                name: "IX_Games_BlackNormalizedFirstName_BlackNormalizedLastName",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_WhiteNormalizedFirstName_WhiteNormalizedLastName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedFirstName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedFullName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedLastName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedFirstName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedFullName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedLastName",
                table: "StagingGames");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedFirstName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedFullName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BlackNormalizedLastName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedFirstName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedFullName",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WhiteNormalizedLastName",
                table: "Games");

            migrationBuilder.AddColumn<Guid>(
                name: "BlackPlayerId",
                table: "StagingGames",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportSessionId",
                table: "StagingGames",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "WhitePlayerId",
                table: "StagingGames",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BlackPlayerId",
                table: "Games",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhitePlayerId",
                table: "Games",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    NormalizedFirstName = table.Column<string>(type: "text", nullable: true),
                    NormalizedFullName = table.Column<string>(type: "text", nullable: false),
                    NormalizedLastName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StagingImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    PromotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingImportSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingImportSessions_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_ImportSessionId",
                table: "StagingGames",
                column: "ImportSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_Black",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "ImportSessionId", "Black" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_GameHash",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "ImportSessionId", "GameHash" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_ImportSessionId_White",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "ImportSessionId", "White" });

            migrationBuilder.CreateIndex(
                name: "IX_Games_BlackPlayerId",
                table: "Games",
                column: "BlackPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_WhitePlayerId",
                table: "Games",
                column: "WhitePlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_NormalizedFirstName",
                table: "Players",
                column: "NormalizedFirstName");

            migrationBuilder.CreateIndex(
                name: "IX_Players_NormalizedFullName",
                table: "Players",
                column: "NormalizedFullName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Players_NormalizedLastName",
                table: "Players",
                column: "NormalizedLastName");

            migrationBuilder.CreateIndex(
                name: "IX_StagingImportSessions_ExpiresAtUtc",
                table: "StagingImportSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StagingImportSessions_OwnerUserId_CreatedAtUtc",
                table: "StagingImportSessions",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Players_BlackPlayerId",
                table: "Games",
                column: "BlackPlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Games_Players_WhitePlayerId",
                table: "Games",
                column: "WhitePlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StagingGames_StagingImportSessions_ImportSessionId",
                table: "StagingGames",
                column: "ImportSessionId",
                principalTable: "StagingImportSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
