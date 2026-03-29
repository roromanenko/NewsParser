using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefactorEntityTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "sources",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "raw_articles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publish_logs",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                table: "publications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "articles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");

            migrationBuilder.AlterColumn<string>(
                name: "Sentiment",
                table: "articles",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "sources",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "raw_articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publish_logs",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "publications",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                table: "publications",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Sentiment",
                table: "articles",
                type: "character varying(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
