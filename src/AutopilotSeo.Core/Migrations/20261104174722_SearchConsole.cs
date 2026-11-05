using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutopilotSeo.Core.Migrations
{
    /// <inheritdoc />
    public partial class SearchConsole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GscConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    GoogleEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Property = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NeedsReconnect = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GscConnections_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GscDaily",
                columns: table => new
                {
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscDaily", x => new { x.SiteId, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "GscPageDaily",
                columns: table => new
                {
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscPageDaily", x => new { x.SiteId, x.Date, x.PageId });
                });

            migrationBuilder.CreateTable(
                name: "GscPages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GscQueries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscQueries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GscQueryPageDaily",
                columns: table => new
                {
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    QueryId = table.Column<int>(type: "int", nullable: false),
                    PageId = table.Column<int>(type: "int", nullable: false),
                    Device = table.Column<byte>(type: "tinyint", nullable: false),
                    Clicks = table.Column<int>(type: "int", nullable: false),
                    Impressions = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GscQueryPageDaily", x => new { x.SiteId, x.Date, x.QueryId, x.PageId, x.Device });
                });

            migrationBuilder.CreateIndex(
                name: "IX_GscConnections_SiteId",
                table: "GscConnections",
                column: "SiteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GscPages_SiteId",
                table: "GscPages",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_GscQueries_SiteId",
                table: "GscQueries",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GscConnections");

            migrationBuilder.DropTable(
                name: "GscDaily");

            migrationBuilder.DropTable(
                name: "GscPageDaily");

            migrationBuilder.DropTable(
                name: "GscPages");

            migrationBuilder.DropTable(
                name: "GscQueries");

            migrationBuilder.DropTable(
                name: "GscQueryPageDaily");
        }
    }
}
