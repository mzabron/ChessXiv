using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DropMoveEvalColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlackEval",
                table: "StagingMoves");

            migrationBuilder.DropColumn(
                name: "WhiteEval",
                table: "StagingMoves");

            migrationBuilder.DropColumn(
                name: "BlackEval",
                table: "Moves");

            migrationBuilder.DropColumn(
                name: "WhiteEval",
                table: "Moves");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BlackEval",
                table: "StagingMoves",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WhiteEval",
                table: "StagingMoves",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "BlackEval",
                table: "Moves",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "WhiteEval",
                table: "Moves",
                type: "double precision",
                nullable: true);
        }
    }
}
