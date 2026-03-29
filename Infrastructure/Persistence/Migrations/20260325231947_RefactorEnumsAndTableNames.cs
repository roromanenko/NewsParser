using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorEnumsAndTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Article_RawArticle_RawArticleId",
                table: "Article");

            migrationBuilder.DropForeignKey(
                name: "FK_Publication_Article_ArticleId",
                table: "Publication");

            migrationBuilder.DropForeignKey(
                name: "FK_Publication_User_EditorId",
                table: "Publication");

            migrationBuilder.DropForeignKey(
                name: "FK_PublishLog_Publication_PublicationId",
                table: "PublishLog");

            migrationBuilder.DropForeignKey(
                name: "FK_RawArticle_Source_SourceId",
                table: "RawArticle");

            migrationBuilder.DropPrimaryKey(
                name: "PK_User",
                table: "User");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Source",
                table: "Source");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RawArticle",
                table: "RawArticle");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PublishLog",
                table: "PublishLog");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Publication",
                table: "Publication");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Article",
                table: "Article");

            migrationBuilder.RenameTable(
                name: "User",
                newName: "users");

            migrationBuilder.RenameTable(
                name: "Source",
                newName: "sources");

            migrationBuilder.RenameTable(
                name: "RawArticle",
                newName: "raw_articles");

            migrationBuilder.RenameTable(
                name: "PublishLog",
                newName: "publish_logs");

            migrationBuilder.RenameTable(
                name: "Publication",
                newName: "publications");

            migrationBuilder.RenameTable(
                name: "Article",
                newName: "articles");

            migrationBuilder.RenameIndex(
                name: "IX_User_Email",
                table: "users",
                newName: "IX_users_Email");

            migrationBuilder.RenameIndex(
                name: "IX_Source_Url",
                table: "sources",
                newName: "IX_sources_Url");

            migrationBuilder.RenameIndex(
                name: "IX_Source_IsActive",
                table: "sources",
                newName: "IX_sources_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_RawArticle_Status",
                table: "raw_articles",
                newName: "IX_raw_articles_Status");

            migrationBuilder.RenameIndex(
                name: "IX_RawArticle_SourceId_ExternalId",
                table: "raw_articles",
                newName: "IX_raw_articles_SourceId_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_PublishLog_PublicationId",
                table: "publish_logs",
                newName: "IX_publish_logs_PublicationId");

            migrationBuilder.RenameIndex(
                name: "IX_Publication_Status",
                table: "publications",
                newName: "IX_publications_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Publication_Platform",
                table: "publications",
                newName: "IX_publications_Platform");

            migrationBuilder.RenameIndex(
                name: "IX_Publication_EditorId",
                table: "publications",
                newName: "IX_publications_EditorId");

            migrationBuilder.RenameIndex(
                name: "IX_Publication_ArticleId",
                table: "publications",
                newName: "IX_publications_ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_Article_Status",
                table: "articles",
                newName: "IX_articles_Status");

            migrationBuilder.RenameIndex(
                name: "IX_Article_RawArticleId",
                table: "articles",
                newName: "IX_articles_RawArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_Article_ProcessedAt",
                table: "articles",
                newName: "IX_articles_ProcessedAt");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "sources",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "raw_articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Language",
                table: "raw_articles",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publish_logs",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publications",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                table: "publications",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Sentiment",
                table: "articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddPrimaryKey(
                name: "PK_users",
                table: "users",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_sources",
                table: "sources",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_raw_articles",
                table: "raw_articles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_publish_logs",
                table: "publish_logs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_publications",
                table: "publications",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_articles",
                table: "articles",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_articles_raw_articles_RawArticleId",
                table: "articles",
                column: "RawArticleId",
                principalTable: "raw_articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_publications_articles_ArticleId",
                table: "publications",
                column: "ArticleId",
                principalTable: "articles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_publications_users_EditorId",
                table: "publications",
                column: "EditorId",
                principalTable: "users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_publish_logs_publications_PublicationId",
                table: "publish_logs",
                column: "PublicationId",
                principalTable: "publications",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_raw_articles_sources_SourceId",
                table: "raw_articles",
                column: "SourceId",
                principalTable: "sources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_articles_raw_articles_RawArticleId",
                table: "articles");

            migrationBuilder.DropForeignKey(
                name: "FK_publications_articles_ArticleId",
                table: "publications");

            migrationBuilder.DropForeignKey(
                name: "FK_publications_users_EditorId",
                table: "publications");

            migrationBuilder.DropForeignKey(
                name: "FK_publish_logs_publications_PublicationId",
                table: "publish_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_raw_articles_sources_SourceId",
                table: "raw_articles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_users",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_sources",
                table: "sources");

            migrationBuilder.DropPrimaryKey(
                name: "PK_raw_articles",
                table: "raw_articles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_publish_logs",
                table: "publish_logs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_publications",
                table: "publications");

            migrationBuilder.DropPrimaryKey(
                name: "PK_articles",
                table: "articles");

            migrationBuilder.RenameTable(
                name: "users",
                newName: "User");

            migrationBuilder.RenameTable(
                name: "sources",
                newName: "Source");

            migrationBuilder.RenameTable(
                name: "raw_articles",
                newName: "RawArticle");

            migrationBuilder.RenameTable(
                name: "publish_logs",
                newName: "PublishLog");

            migrationBuilder.RenameTable(
                name: "publications",
                newName: "Publication");

            migrationBuilder.RenameTable(
                name: "articles",
                newName: "Article");

            migrationBuilder.RenameIndex(
                name: "IX_users_Email",
                table: "User",
                newName: "IX_User_Email");

            migrationBuilder.RenameIndex(
                name: "IX_sources_Url",
                table: "Source",
                newName: "IX_Source_Url");

            migrationBuilder.RenameIndex(
                name: "IX_sources_IsActive",
                table: "Source",
                newName: "IX_Source_IsActive");

            migrationBuilder.RenameIndex(
                name: "IX_raw_articles_Status",
                table: "RawArticle",
                newName: "IX_RawArticle_Status");

            migrationBuilder.RenameIndex(
                name: "IX_raw_articles_SourceId_ExternalId",
                table: "RawArticle",
                newName: "IX_RawArticle_SourceId_ExternalId");

            migrationBuilder.RenameIndex(
                name: "IX_publish_logs_PublicationId",
                table: "PublishLog",
                newName: "IX_PublishLog_PublicationId");

            migrationBuilder.RenameIndex(
                name: "IX_publications_Status",
                table: "Publication",
                newName: "IX_Publication_Status");

            migrationBuilder.RenameIndex(
                name: "IX_publications_Platform",
                table: "Publication",
                newName: "IX_Publication_Platform");

            migrationBuilder.RenameIndex(
                name: "IX_publications_EditorId",
                table: "Publication",
                newName: "IX_Publication_EditorId");

            migrationBuilder.RenameIndex(
                name: "IX_publications_ArticleId",
                table: "Publication",
                newName: "IX_Publication_ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_articles_Status",
                table: "Article",
                newName: "IX_Article_Status");

            migrationBuilder.RenameIndex(
                name: "IX_articles_RawArticleId",
                table: "Article",
                newName: "IX_Article_RawArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_articles_ProcessedAt",
                table: "Article",
                newName: "IX_Article_ProcessedAt");

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "Source",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "RawArticle",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Language",
                table: "RawArticle",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "PublishLog",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Publication",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<int>(
                name: "Platform",
                table: "Publication",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "Article",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<int>(
                name: "Sentiment",
                table: "Article",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_User",
                table: "User",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Source",
                table: "Source",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RawArticle",
                table: "RawArticle",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PublishLog",
                table: "PublishLog",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Publication",
                table: "Publication",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Article",
                table: "Article",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Article_RawArticle_RawArticleId",
                table: "Article",
                column: "RawArticleId",
                principalTable: "RawArticle",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Publication_Article_ArticleId",
                table: "Publication",
                column: "ArticleId",
                principalTable: "Article",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Publication_User_EditorId",
                table: "Publication",
                column: "EditorId",
                principalTable: "User",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PublishLog_Publication_PublicationId",
                table: "PublishLog",
                column: "PublicationId",
                principalTable: "Publication",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RawArticle_Source_SourceId",
                table: "RawArticle",
                column: "SourceId",
                principalTable: "Source",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
