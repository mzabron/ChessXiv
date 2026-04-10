using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    IsMaster = table.Column<bool>(type: "boolean", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Games");
        }
    }
}
