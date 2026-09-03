using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IndexUserDatabaseGamesForDefaultOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseGames_UserDatabaseId_AddedAtUtc_GameId",
                table: "UserDatabaseGames",
                columns: new[] { "UserDatabaseId", "AddedAtUtc", "GameId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserDatabaseGames_UserDatabaseId_AddedAtUtc_GameId",
                table: "UserDatabaseGames");
        }
    }
}
