using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OptimizePromotionHotPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Positions_Fen",
                table: "Positions");

            migrationBuilder.CreateIndex(
                name: "IX_StagingGames_OwnerUserId_Id",
                table: "StagingGames",
                columns: new[] { "OwnerUserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StagingGames_OwnerUserId_Id",
                table: "StagingGames");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Fen",
                table: "Positions",
                column: "Fen");
        }
    }
}
