using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CompactPositionStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing position rows cannot be migrated in place: PosKey needs the chess
            // engine to compute, and NextMove is LastMove shifted by one ply, so renaming
            // the column would leave every row quietly wrong. The rows are dropped instead.
            //
            // Games and their PGNs are kept - they are the source of truth and expensive to
            // re-upload. Regenerate the positions afterwards with:
            //     dotnet run --project src/ChessXiv.Cli -- --rebuild-positions
            // Until that runs, position search and the opening tree return nothing; game
            // lists and replay keep working.
            migrationBuilder.Sql("""TRUNCATE TABLE "StagingPositions", "StagingMoves", "StagingGames";""");
            migrationBuilder.Sql("""TRUNCATE TABLE "Positions";""");

            migrationBuilder.DropTable(
                name: "Moves");

            migrationBuilder.DropTable(
                name: "StagingMoves");

            migrationBuilder.DropPrimaryKey(
                name: "PK_StagingPositions",
                table: "StagingPositions");

            migrationBuilder.DropIndex(
                name: "IX_StagingPositions_FenHash",
                table: "StagingPositions");

            migrationBuilder.DropIndex(
                name: "IX_StagingPositions_StagingGameId_PlyCount",
                table: "StagingPositions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Positions",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_Fen",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_FenHash",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_GameId_PlyCount",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "Fen",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "FenHash",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "SideToMove",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Fen",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "FenHash",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "SideToMove",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "IsMaster",
                table: "Games");

            migrationBuilder.RenameColumn(
                name: "LastMove",
                table: "StagingPositions",
                newName: "NextMove");

            migrationBuilder.RenameColumn(
                name: "LastMove",
                table: "Positions",
                newName: "NextMove");

            migrationBuilder.AlterColumn<short>(
                name: "PlyCount",
                table: "StagingPositions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<byte[]>(
                name: "PosKey",
                table: "StagingPositions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte>(
                name: "Result",
                table: "StagingPositions",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AlterColumn<short>(
                name: "PlyCount",
                table: "Positions",
                type: "smallint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<byte[]>(
                name: "PosKey",
                table: "Positions",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte>(
                name: "Result",
                table: "Positions",
                type: "smallint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StagingPositions",
                table: "StagingPositions",
                columns: new[] { "StagingGameId", "PlyCount" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_Positions",
                table: "Positions",
                columns: new[] { "GameId", "PlyCount" });

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_PosKey",
                table: "StagingPositions",
                column: "PosKey")
                .Annotation("Npgsql:IndexInclude", new[] { "NextMove", "Result" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PosKey",
                table: "Positions",
                column: "PosKey")
                .Annotation("Npgsql:IndexInclude", new[] { "NextMove", "Result" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StagingPositions",
                table: "StagingPositions");

            migrationBuilder.DropIndex(
                name: "IX_StagingPositions_PosKey",
                table: "StagingPositions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Positions",
                table: "Positions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_PosKey",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "PosKey",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "StagingPositions");

            migrationBuilder.DropColumn(
                name: "PosKey",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Result",
                table: "Positions");

            migrationBuilder.RenameColumn(
                name: "NextMove",
                table: "StagingPositions",
                newName: "LastMove");

            migrationBuilder.RenameColumn(
                name: "NextMove",
                table: "Positions",
                newName: "LastMove");

            migrationBuilder.AlterColumn<int>(
                name: "PlyCount",
                table: "StagingPositions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "StagingPositions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Fen",
                table: "StagingPositions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FenHash",
                table: "StagingPositions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<char>(
                name: "SideToMove",
                table: "StagingPositions",
                type: "character(1)",
                nullable: false,
                defaultValue: '\0');

            migrationBuilder.AlterColumn<int>(
                name: "PlyCount",
                table: "Positions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(short),
                oldType: "smallint");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "Positions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Fen",
                table: "Positions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FenHash",
                table: "Positions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<char>(
                name: "SideToMove",
                table: "Positions",
                type: "character(1)",
                nullable: false,
                defaultValue: '\0');

            migrationBuilder.AddColumn<bool>(
                name: "IsMaster",
                table: "Games",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddPrimaryKey(
                name: "PK_StagingPositions",
                table: "StagingPositions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Positions",
                table: "Positions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "Moves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlackClk = table.Column<string>(type: "text", nullable: true),
                    BlackMove = table.Column<string>(type: "text", nullable: true),
                    MoveNumber = table.Column<int>(type: "integer", nullable: false),
                    WhiteClk = table.Column<string>(type: "text", nullable: true),
                    WhiteMove = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Moves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Moves_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagingMoves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StagingGameId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlackClk = table.Column<string>(type: "text", nullable: true),
                    BlackMove = table.Column<string>(type: "text", nullable: true),
                    MoveNumber = table.Column<int>(type: "integer", nullable: false),
                    WhiteClk = table.Column<string>(type: "text", nullable: true),
                    WhiteMove = table.Column<string>(type: "text", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_FenHash",
                table: "StagingPositions",
                column: "FenHash");

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_StagingGameId_PlyCount",
                table: "StagingPositions",
                columns: new[] { "StagingGameId", "PlyCount" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Fen",
                table: "Positions",
                column: "Fen");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_FenHash",
                table: "Positions",
                column: "FenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_GameId_PlyCount",
                table: "Positions",
                columns: new[] { "GameId", "PlyCount" });

            migrationBuilder.CreateIndex(
                name: "IX_Moves_GameId",
                table: "Moves",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_StagingMoves_StagingGameId",
                table: "StagingMoves",
                column: "StagingGameId");
        }
    }
}
