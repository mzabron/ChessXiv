using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayersYearMoveCountExplorer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BlackPlayerId",
                table: "Games",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoveCount",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "WhitePlayerId",
                table: "Games",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "Games",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    NormalizedFullName = table.Column<string>(type: "text", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: true),
                    LastName = table.Column<string>(type: "text", nullable: true),
                    NormalizedFirstName = table.Column<string>(type: "text", nullable: true),
                    NormalizedLastName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Games_BlackPlayerId",
                table: "Games",
                column: "BlackPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_MoveCount",
                table: "Games",
                column: "MoveCount");

            migrationBuilder.CreateIndex(
                name: "IX_Games_WhitePlayerId",
                table: "Games",
                column: "WhitePlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_Year_Id",
                table: "Games",
                columns: new[] { "Year", "Id" });

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Games_Players_BlackPlayerId",
                table: "Games");

            migrationBuilder.DropForeignKey(
                name: "FK_Games_Players_WhitePlayerId",
                table: "Games");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropIndex(
                name: "IX_Games_BlackPlayerId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_MoveCount",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_WhitePlayerId",
                table: "Games");

            migrationBuilder.DropIndex(
                name: "IX_Games_Year_Id",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "BlackPlayerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "MoveCount",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "WhitePlayerId",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "Games");
        }
    }
}
