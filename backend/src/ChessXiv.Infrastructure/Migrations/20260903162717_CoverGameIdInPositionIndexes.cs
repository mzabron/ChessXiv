using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <summary>
    /// Adds GameId / StagingGameId to the PosKey covering indexes.
    /// </summary>
    /// <remarks>
    /// The opening tree joins Positions to UserDatabaseGames on GameId to scope a position
    /// to one database. Without GameId in the index that join has to visit the heap for
    /// every matching row, and at the starting position - which every game contains - the
    /// planner rated that worse than a sequential scan and read all 89M rows instead:
    /// 34 seconds on a 1.6M-game database, past the command timeout, so the endpoint
    /// returned 500. With GameId covered, both sides of the join are index-only scans.
    ///
    /// This rebuilds a multi-gigabyte index, so it is slow (tens of minutes on a large
    /// database) and needs a command timeout well above the default - pass one on the
    /// connection string, e.g. "...;Command Timeout=7200". The opening tree is unavailable
    /// while it runs.
    /// </remarks>
    public partial class CoverGameIdInPositionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sorting a large index with the default 64MB spills to disk far more than it
            // needs to. Session-scoped, so it reverts when the migration connection closes.
            migrationBuilder.Sql("SET maintenance_work_mem = '512MB';");

            migrationBuilder.DropIndex(
                name: "IX_StagingPositions_PosKey",
                table: "StagingPositions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_PosKey",
                table: "Positions");

            migrationBuilder.CreateIndex(
                name: "IX_StagingPositions_PosKey",
                table: "StagingPositions",
                column: "PosKey")
                .Annotation("Npgsql:IndexInclude", new[] { "NextMove", "Result", "StagingGameId" });

            migrationBuilder.CreateIndex(
                name: "IX_Positions_PosKey",
                table: "Positions",
                column: "PosKey")
                .Annotation("Npgsql:IndexInclude", new[] { "NextMove", "Result", "GameId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("SET maintenance_work_mem = '512MB';");

            migrationBuilder.DropIndex(
                name: "IX_StagingPositions_PosKey",
                table: "StagingPositions");

            migrationBuilder.DropIndex(
                name: "IX_Positions_PosKey",
                table: "Positions");

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
    }
}
