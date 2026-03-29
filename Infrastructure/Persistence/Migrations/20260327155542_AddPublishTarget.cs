using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_publications_Platform",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "publications");

            migrationBuilder.AddColumn<Guid>(
                name: "PublishTargetId",
                table: "publications",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "publish_targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    Identifier = table.Column<string>(type: "text", nullable: false),
                    SystemPrompt = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_publish_targets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_publications_PublishTargetId",
                table: "publications",
                column: "PublishTargetId");

            migrationBuilder.CreateIndex(
                name: "IX_publish_targets_IsActive",
                table: "publish_targets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_publish_targets_Platform",
                table: "publish_targets",
                column: "Platform");

            migrationBuilder.AddForeignKey(
                name: "FK_publications_publish_targets_PublishTargetId",
                table: "publications",
                column: "PublishTargetId",
                principalTable: "publish_targets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_publications_publish_targets_PublishTargetId",
                table: "publications");

            migrationBuilder.DropTable(
                name: "publish_targets");

            migrationBuilder.DropIndex(
                name: "IX_publications_PublishTargetId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "PublishTargetId",
                table: "publications");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "publications",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_publications_Platform",
                table: "publications",
                column: "Platform");
        }
    }
}
