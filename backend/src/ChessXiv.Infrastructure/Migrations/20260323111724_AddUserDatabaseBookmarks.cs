using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChessXiv.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDatabaseBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserDatabaseBookmarks",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    UserDatabaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDatabaseBookmarks", x => new { x.UserId, x.UserDatabaseId });
                    table.ForeignKey(
                        name: "FK_UserDatabaseBookmarks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDatabaseBookmarks_UserDatabases_UserDatabaseId",
                        column: x => x.UserDatabaseId,
                        principalTable: "UserDatabases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseBookmarks_UserDatabaseId",
                table: "UserDatabaseBookmarks",
                column: "UserDatabaseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDatabaseBookmarks_UserId_CreatedAtUtc",
                table: "UserDatabaseBookmarks",
                columns: new[] { "UserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserDatabaseBookmarks");
        }
    }
}
