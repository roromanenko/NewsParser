using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArticleOwnsEventRelationship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_articles");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AddedToEventAt",
                table: "articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "articles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasReclassified",
                table: "articles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_articles_EventId",
                table: "articles",
                column: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_articles_events_EventId",
                table: "articles",
                column: "EventId",
                principalTable: "events",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_articles_events_EventId",
                table: "articles");

            migrationBuilder.DropIndex(
                name: "IX_articles_EventId",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "AddedToEventAt",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "WasReclassified",
                table: "articles");

            migrationBuilder.CreateTable(
                name: "event_articles",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    WasReclassified = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_articles", x => new { x.EventId, x.ArticleId });
                    table.ForeignKey(
                        name: "FK_event_articles_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_articles_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_event_articles_ArticleId",
                table: "event_articles",
                column: "ArticleId");
        }
    }
}
