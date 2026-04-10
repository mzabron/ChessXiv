using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStagingArea : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "Date",
                table: "UserDatabaseGames",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Event",
                table: "UserDatabaseGames",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Round",
                table: "UserDatabaseGames",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "UserDatabaseGames",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "UserDatabaseGames",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GameHash",
                table: "Games",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "StagingImportSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "StagingGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<string>(type: "text", nullable: false),
                    WhitePlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    BlackPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<string>(type: "text", nullable: true),
                    WhiteTitle = table.Column<string>(type: "text", nullable: true),
                    BlackTitle = table.Column<string>(type: "text", nullable: true),
                    WhiteElo = table.Column<int>(type: "integer", nullable: true),
                    BlackElo = table.Column<int>(type: "integer", nullable: true),
                    Event = table.Column<string>(type: "text", nullable: true),
                    Site = table.Column<string>(type: "text", nullable: true),
                    TimeControl = table.Column<string>(type: "text", nullable: true),
                    ECO = table.Column<string>(type: "text", nullable: true),
                    Opening = table.Column<string>(type: "text", nullable: true),
                    White = table.Column<string>(type: "text", nullable: false),
                    Black = table.Column<string>(type: "text", nullable: false),
                    Result = table.Column<string>(type: "text", nullable: false),
                    Pgn = table.Column<string>(type: "text", nullable: false),
                    MoveCount = table.Column<int>(type: "integer", nullable: false),
                    GameHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingGames_StagingImportSessions_ImportSessionId",
                        column: x => x.ImportSessionId,
                        principalTable: "StagingImportSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagingMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StagingGameId = table.Column<Guid>(type: "uuid", nullable: false),
                    MoveNumber = table.Column<int>(type: "integer", nullable: false),
                    WhiteMove = table.Column<string>(type: "text", nullable: false),
                    BlackMove = table.Column<string>(type: "text", nullable: true),
                    WhiteClk = table.Column<string>(type: "text", nullable: true),
                    BlackClk = table.Column<string>(type: "text", nullable: true),
                    WhiteEval = table.Column<double>(type: "double precision", nullable: true),
                    BlackEval = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingMoves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingMoves_StagingGames_StagingGameId",
                        column: x => x.StagingGameId,
                        principalTable: "StagingGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagingPositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StagingGameId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fen = table.Column<string>(type: "text", nullable: false),
                    FenHash = table.Column<long>(type: "bigint", nullable: false),
                    PlyCount = table.Column<int>(type: "integer", nullable: false),
                    LastMove = table.Column<string>(type: "text", nullable: true),
                    SideToMove = table.Column<char>(type: "character(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagingPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagingPositions_StagingGames_StagingGameId",
                        column: x => x.StagingGameId,
                        principalTable: "StagingGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_GameHash",
                table: "Games",
                column: "GameHash");

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
                name: "IX_StagingImportSessions_ExpiresAtUtc",
                table: "StagingImportSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StagingImportSessions_OwnerUserId_CreatedAtUtc",
                table: "StagingImportSessions",
                columns: new[] { "OwnerUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingMoves_StagingGameId",
                table: "StagingMoves",
                column: "StagingGameId");

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_FenHash",
                table: "StagingPositions",
                column: "FenHash");

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_StagingGameId_PlyCount",
                table: "StagingPositions",
                columns: new[] { "StagingGameId", "PlyCount" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StagingMoves");

            migrationBuilder.DropTable(
                name: "StagingPositions");

            migrationBuilder.DropTable(
                name: "StagingGames");

            migrationBuilder.DropTable(
                name: "StagingImportSessions");

            migrationBuilder.DropIndex(
                name: "IX_Games_GameHash",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "UserDatabaseGames");

            migrationBuilder.DropColumn(
                name: "Event",
                table: "UserDatabaseGames");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "UserDatabaseGames");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "UserDatabaseGames");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "UserDatabaseGames");

            migrationBuilder.DropColumn(
                name: "GameHash",
                table: "Games");
        }
    }
}
