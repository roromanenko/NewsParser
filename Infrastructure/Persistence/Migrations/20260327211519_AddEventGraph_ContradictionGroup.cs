using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventGraph_ContradictionGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_contradictions_articles_ArticleIdA",
                table: "contradictions");

            migrationBuilder.DropForeignKey(
                name: "FK_contradictions_articles_ArticleIdB",
                table: "contradictions");

            migrationBuilder.DropIndex(
                name: "IX_contradictions_ArticleIdA",
                table: "contradictions");

            migrationBuilder.DropIndex(
                name: "IX_contradictions_ArticleIdB",
                table: "contradictions");

            migrationBuilder.DropColumn(
                name: "ArticleIdA",
                table: "contradictions");

            migrationBuilder.DropColumn(
                name: "ArticleIdB",
                table: "contradictions");

            migrationBuilder.CreateTable(
                name: "contradiction_articles",
                columns: table => new
                {
                    ContradictionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contradiction_articles", x => new { x.ContradictionId, x.ArticleId });
                    table.ForeignKey(
                        name: "FK_contradiction_articles_articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_contradiction_articles_contradictions_ContradictionId",
                        column: x => x.ContradictionId,
                        principalTable: "contradictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_contradiction_articles_ArticleId",
                table: "contradiction_articles",
                column: "ArticleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contradiction_articles");

            migrationBuilder.AddColumn<Guid>(
                name: "ArticleIdA",
                table: "contradictions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ArticleIdB",
                table: "contradictions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_ArticleIdA",
                table: "contradictions",
                column: "ArticleIdA");

            migrationBuilder.CreateIndex(
                name: "IX_contradictions_ArticleIdB",
                table: "contradictions",
                column: "ArticleIdB");

            migrationBuilder.AddForeignKey(
                name: "FK_contradictions_articles_ArticleIdA",
                table: "contradictions",
                column: "ArticleIdA",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_contradictions_articles_ArticleIdB",
                table: "contradictions",
                column: "ArticleIdB",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
