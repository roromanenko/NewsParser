using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleKeyFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeyFacts",
                table: "articles",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddForeignKey(
                name: "FK_publications_events_EventId",
                table: "publications",
                column: "EventId",
                principalTable: "events",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_publications_events_EventId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "KeyFacts",
                table: "articles");
        }
    }
}
