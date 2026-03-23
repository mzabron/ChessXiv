using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTierAndPositionFenIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserTier",
                table: "AspNetUsers",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Free");

            migrationBuilder.CreateIndex(
                name: "IX_Positions_Fen",
                table: "Positions",
                column: "Fen");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Positions_Fen",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "UserTier",
                table: "AspNetUsers");
        }
    }
}
