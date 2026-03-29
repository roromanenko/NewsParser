using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceAndPublishLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "SourceUrl",
                table: "RawArticle",
                newName: "OriginalUrl");

            migrationBuilder.RenameColumn(
                name: "Source",
                table: "RawArticle",
                newName: "Language");

            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "RawArticle",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceId",
                table: "RawArticle",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "Publication",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Publication",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Article",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "Article",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PublishLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublishLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublishLog_Publication_PublicationId",
                        column: x => x.PublicationId,
                        principalTable: "Publication",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Source",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastFetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Source", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_User_Email",
                table: "User",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawArticle_SourceId_ExternalId",
                table: "RawArticle",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RawArticle_Status",
                table: "RawArticle",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Publication_Platform",
                table: "Publication",
                column: "Platform");

            migrationBuilder.CreateIndex(
                name: "IX_Publication_Status",
                table: "Publication",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Article_ProcessedAt",
                table: "Article",
                column: "ProcessedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Article_Status",
                table: "Article",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PublishLog_PublicationId",
                table: "PublishLog",
                column: "PublicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Source_IsActive",
                table: "Source",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Source_Url",
                table: "Source",
                column: "Url",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RawArticle_Source_SourceId",
                table: "RawArticle",
                column: "SourceId",
                principalTable: "Source",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RawArticle_Source_SourceId",
                table: "RawArticle");

            migrationBuilder.DropTable(
                name: "PublishLog");

            migrationBuilder.DropTable(
                name: "Source");

            migrationBuilder.DropIndex(
                name: "IX_User_Email",
                table: "User");

            migrationBuilder.DropIndex(
                name: "IX_RawArticle_SourceId_ExternalId",
                table: "RawArticle");

            migrationBuilder.DropIndex(
                name: "IX_RawArticle_Status",
                table: "RawArticle");

            migrationBuilder.DropIndex(
                name: "IX_Publication_Platform",
                table: "Publication");

            migrationBuilder.DropIndex(
                name: "IX_Publication_Status",
                table: "Publication");

            migrationBuilder.DropIndex(
                name: "IX_Article_ProcessedAt",
                table: "Article");

            migrationBuilder.DropIndex(
                name: "IX_Article_Status",
                table: "Article");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "RawArticle");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "RawArticle");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Publication");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Publication");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Article");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "Article");

            migrationBuilder.RenameColumn(
                name: "OriginalUrl",
                table: "RawArticle",
                newName: "SourceUrl");

            migrationBuilder.RenameColumn(
                name: "Language",
                table: "RawArticle",
                newName: "Source");
        }
    }
}
