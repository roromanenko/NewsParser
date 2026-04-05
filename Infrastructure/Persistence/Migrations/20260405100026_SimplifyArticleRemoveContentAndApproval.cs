using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyArticleRemoveContentAndApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE articles SET \"Status\" = 'AnalysisDone' WHERE \"Status\" IN ('Approved', 'Published');");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "RejectedByEditorId",
                table: "articles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Content",
                table: "articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByEditorId",
                table: "articles",
                type: "uuid",
                nullable: true);
        }
    }
}
