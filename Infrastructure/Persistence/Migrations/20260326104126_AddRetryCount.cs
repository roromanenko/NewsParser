using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "publications");

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "raw_articles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RejectedByEditorId",
                table: "articles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "articles",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "raw_articles");

            migrationBuilder.DropColumn(
                name: "RejectedByEditorId",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "articles");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "articles");

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "publications",
                type: "text",
                nullable: true);
        }
    }
}
