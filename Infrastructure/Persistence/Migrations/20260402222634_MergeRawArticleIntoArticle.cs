using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
	/// <inheritdoc />
	public partial class MergeRawArticleIntoArticle : Migration
	{
		/// <inheritdoc />
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			// Step 1 — add new columns to articles and events
			migrationBuilder.AddColumn<int>(
				name: "ArticleCount",
				table: "events",
				type: "integer",
				nullable: false,
				defaultValue: 0);

			migrationBuilder.AddColumn<Vector>(
				name: "Embedding",
				table: "articles",
				type: "vector(768)",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "ExternalId",
				table: "articles",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "OriginalContent",
				table: "articles",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<string>(
				name: "OriginalUrl",
				table: "articles",
				type: "text",
				nullable: true);

			migrationBuilder.AddColumn<DateTimeOffset>(
				name: "PublishedAt",
				table: "articles",
				type: "timestamp with time zone",
				nullable: true);

			migrationBuilder.AddColumn<Guid>(
				name: "SourceId",
				table: "articles",
				type: "uuid",
				nullable: true);

			// Step 2 — backfill source fields from raw_articles using the still-present raw_article_id FK
			migrationBuilder.Sql(@"
			    UPDATE articles
			    SET ""OriginalContent"" = r.""Content"",
			        ""SourceId""        = r.""SourceId"",
			        ""OriginalUrl""     = r.""OriginalUrl"",
			        ""PublishedAt""     = r.""PublishedAt"",
			        ""ExternalId""      = r.""ExternalId""
			    FROM raw_articles r
			    WHERE articles.""RawArticleId"" = r.""Id"";
			");

			// Step 3 — drop the FK and the raw_article_id column
			migrationBuilder.DropForeignKey(
				name: "FK_articles_raw_articles_RawArticleId",
				table: "articles");

			migrationBuilder.DropIndex(
				name: "IX_articles_RawArticleId",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "RawArticleId",
				table: "articles");

			// Step 4 — add indexes and new FK
			migrationBuilder.CreateIndex(
				name: "IX_articles_Embedding",
				table: "articles",
				column: "Embedding")
				.Annotation("Npgsql:IndexMethod", "hnsw")
				.Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

			migrationBuilder.CreateIndex(
				name: "IX_articles_SourceId_ExternalId",
				table: "articles",
				columns: new[] { "SourceId", "ExternalId" },
				unique: true,
				filter: "\"SourceId\" IS NOT NULL AND \"ExternalId\" IS NOT NULL");

			migrationBuilder.AddForeignKey(
				name: "FK_articles_sources_SourceId",
				table: "articles",
				column: "SourceId",
				principalTable: "sources",
				principalColumn: "Id");
		}

		/// <inheritdoc />
		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropForeignKey(
				name: "FK_articles_sources_SourceId",
				table: "articles");

			migrationBuilder.DropIndex(
				name: "IX_articles_Embedding",
				table: "articles");

			migrationBuilder.DropIndex(
				name: "IX_articles_SourceId_ExternalId",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "ArticleCount",
				table: "events");

			migrationBuilder.DropColumn(
				name: "Embedding",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "ExternalId",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "OriginalContent",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "OriginalUrl",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "PublishedAt",
				table: "articles");

			migrationBuilder.DropColumn(
				name: "SourceId",
				table: "articles");

			migrationBuilder.AddColumn<Guid>(
				name: "RawArticleId",
				table: "articles",
				type: "uuid",
				nullable: false,
				defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

			migrationBuilder.CreateIndex(
				name: "IX_articles_RawArticleId",
				table: "articles",
				column: "RawArticleId",
				unique: true);

			migrationBuilder.AddForeignKey(
				name: "FK_articles_raw_articles_RawArticleId",
				table: "articles",
				column: "RawArticleId",
				principalTable: "raw_articles",
				principalColumn: "Id",
				onDelete: ReferentialAction.Cascade);
		}
	}
}
