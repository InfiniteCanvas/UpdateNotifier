using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateNotifier.Migrations
{
    /// <inheritdoc />
    public partial class ContainerSplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Container-split additions: PendingNotifications is the durable DM queue the bot
            // container polls (with a dead-letter state and a fetch index over it), PrivilegedUsers
            // is the privilege cache the bot pushes. Both are brand-new tables: keep the
            // scaffolded operations for them verbatim.

            migrationBuilder.CreateTable(
                name: "PendingNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DiscordUserId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivilegedUsers",
                columns: table => new
                {
                    UserId = table.Column<ulong>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivilegedUsers", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingNotifications_Status_Id",
                table: "PendingNotifications",
                columns: new[] { "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Plain new tables with no other table referencing them: drop and be done.
            migrationBuilder.DropTable(
                name: "PendingNotifications");

            migrationBuilder.DropTable(
                name: "PrivilegedUsers");
        }
    }
}
