using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UpdateNotifier.Migrations
{
    /// <inheritdoc />
    public partial class AddGameThumbnailUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Purely additive nullable column: safe to apply on live data and safe to drop again.

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "Games",
                type: "TEXT",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "Games");
        }
    }
}
