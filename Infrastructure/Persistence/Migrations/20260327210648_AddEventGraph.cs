using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalMessageId",
                table: "publish_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentPublicationId",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(768)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "contradictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleIdA = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleIdB = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contradictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_contradictions_articles_ArticleIdA",
                        column: x => x.ArticleIdA,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradictions_articles_ArticleIdB",
                        column: x => x.ArticleIdB,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradictions_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_articles",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "event_updates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactSummary = table.Column<string>(type: "text", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_updates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_event_updates_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_updates_events_EventId",
                        column: x => x.EventId,
                        principalTable: "events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publications_EventId",
                table: "publications",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_publications_ParentPublicationId",
                table: "publications",
                column: "ParentPublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_ArticleIdA",
                table: "contradictions",
                column: "ArticleIdA");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_ArticleIdB",
                table: "contradictions",
                column: "ArticleIdB");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_EventId",
                table: "contradictions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_IsResolved",
                table: "contradictions",
                column: "IsResolved");

            migrationBuilder.CreateIndex(
                name: "IX_event_articles_ArticleId",
                table: "event_articles",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_event_updates_ArticleId",
                table: "event_updates",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_event_updates_EventId",
                table: "event_updates",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_event_updates_IsPublished",
                table: "event_updates",
                column: "IsPublished");

            migrationBuilder.CreateIndex(
                name: "IX_events_FirstSeenAt",
                table: "events",
                column: "FirstSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_events_LastUpdatedAt",
                table: "events",
                column: "LastUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_events_Status",
                table: "events",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_publications_publications_ParentPublicationId",
                table: "publications",
                column: "ParentPublicationId",
                principalTable: "publications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_publications_publications_ParentPublicationId",
                table: "publications");

            migrationBuilder.DropTable(
                name: "contradictions");

            migrationBuilder.DropTable(
                name: "event_articles");

            migrationBuilder.DropTable(
                name: "event_updates");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropIndex(
                name: "IX_publications_EventId",
                table: "publications");

            migrationBuilder.DropIndex(
                name: "IX_publications_ParentPublicationId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "ExternalMessageId",
                table: "publish_logs");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "ParentPublicationId",
                table: "publications");
        }
    }
}
