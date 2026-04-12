using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicationPipelineRedesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "publications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "publications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByEditorId",
                table: "publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedMediaFileIds",
                table: "publications",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql("""UPDATE publications SET "Status" = 'Created' WHERE "Status" = 'Pending'""");
            migrationBuilder.Sql("""UPDATE events SET "Status" = 'Active' WHERE "Status" IN ('Approved', 'Resolved')""");
            migrationBuilder.Sql("""UPDATE events SET "Status" = 'Archived' WHERE "Status" = 'Rejected'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "ReviewedByEditorId",
                table: "publications");

            migrationBuilder.DropColumn(
                name: "SelectedMediaFileIds",
                table: "publications");

            migrationBuilder.Sql("""UPDATE publications SET "Status" = 'Pending' WHERE "Status" = 'Created'""");
            migrationBuilder.Sql("""UPDATE events SET "Status" = 'Approved' WHERE "Status" = 'Active'""");
            migrationBuilder.Sql("""UPDATE events SET "Status" = 'Rejected' WHERE "Status" = 'Archived'""");
        }
    }
}
