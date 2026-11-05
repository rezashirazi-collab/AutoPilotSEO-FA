using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutopilotSeo.Core.Migrations
{
    /// <inheritdoc />
    public partial class TechnicalHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrawlLinks",
                columns: table => new
                {
                    CrawlId = table.Column<int>(type: "int", nullable: false),
                    FromId = table.Column<long>(type: "bigint", nullable: false),
                    ToId = table.Column<long>(type: "bigint", nullable: false),
                    Anchor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlLinks", x => new { x.CrawlId, x.FromId, x.ToId });
                });

            migrationBuilder.CreateTable(
                name: "CrawlPages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CrawlId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    RedirectTo = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Depth = table.Column<int>(type: "int", nullable: false),
                    InSitemap = table.Column<bool>(type: "bit", nullable: false),
                    BlockedByRobots = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    H1 = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Canonical = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Noindex = table.Column<bool>(type: "bit", nullable: false),
                    WordCount = table.Column<int>(type: "int", nullable: false),
                    Inlinks = table.Column<int>(type: "int", nullable: false),
                    ResponseMs = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlPages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Crawls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PagesCrawled = table.Column<int>(type: "int", nullable: false),
                    HitPageLimit = table.Column<bool>(type: "bit", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crawls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Crawls_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CrawlId = table.Column<int>(type: "int", nullable: false),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Severity = table.Column<byte>(type: "tinyint", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UrlInspections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SiteId = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Verdict = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CoverageState = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IndexingState = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    RobotsTxtState = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    PageFetchState = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    GoogleCanonical = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    UserCanonical = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LastCrawlTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlInspections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_SiteId_ResolvedAt",
                table: "Alerts",
                columns: new[] { "SiteId", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlPages_CrawlId",
                table: "CrawlPages",
                column: "CrawlId");

            migrationBuilder.CreateIndex(
                name: "IX_Crawls_SiteId_Id",
                table: "Crawls",
                columns: new[] { "SiteId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_SiteId_CrawlId",
                table: "Issues",
                columns: new[] { "SiteId", "CrawlId" });

            migrationBuilder.CreateIndex(
                name: "IX_UrlInspections_SiteId",
                table: "UrlInspections",
                column: "SiteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "CrawlLinks");

            migrationBuilder.DropTable(
                name: "CrawlPages");

            migrationBuilder.DropTable(
                name: "Crawls");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "UrlInspections");
        }
    }
}
