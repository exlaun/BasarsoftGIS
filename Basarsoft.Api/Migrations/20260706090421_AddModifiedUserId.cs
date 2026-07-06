using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModifiedUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "modified_user_id",
                table: "tbl_polygon",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "modified_user_id",
                table: "tbl_point",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "modified_user_id",
                table: "tbl_line",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tbl_polygon_modified_user_id",
                table: "tbl_polygon",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_point_modified_user_id",
                table: "tbl_point",
                column: "modified_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tbl_line_modified_user_id",
                table: "tbl_line",
                column: "modified_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_tbl_line_users_modified_user_id",
                table: "tbl_line",
                column: "modified_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tbl_point_users_modified_user_id",
                table: "tbl_point",
                column: "modified_user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_tbl_polygon_users_modified_user_id",
                table: "tbl_polygon",
                column: "modified_user_id",
                principalTable: "users",
                principalColumn: "id");

            // Hand-edited backfill: shapes are only ever visible to (and editable by) their owner, so
            // every legacy row's last modifier can only have been its creator. Seeding from user_id
            // means the UI never renders an "unknown editor" state — the same precedent as the
            // modified_date = created_at backfill in AddGeometryAuditColumns.
            migrationBuilder.Sql("UPDATE tbl_point SET modified_user_id = user_id;");
            migrationBuilder.Sql("UPDATE tbl_line SET modified_user_id = user_id;");
            migrationBuilder.Sql("UPDATE tbl_polygon SET modified_user_id = user_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tbl_line_users_modified_user_id",
                table: "tbl_line");

            migrationBuilder.DropForeignKey(
                name: "fk_tbl_point_users_modified_user_id",
                table: "tbl_point");

            migrationBuilder.DropForeignKey(
                name: "fk_tbl_polygon_users_modified_user_id",
                table: "tbl_polygon");

            migrationBuilder.DropIndex(
                name: "ix_tbl_polygon_modified_user_id",
                table: "tbl_polygon");

            migrationBuilder.DropIndex(
                name: "ix_tbl_point_modified_user_id",
                table: "tbl_point");

            migrationBuilder.DropIndex(
                name: "ix_tbl_line_modified_user_id",
                table: "tbl_line");

            migrationBuilder.DropColumn(
                name: "modified_user_id",
                table: "tbl_polygon");

            migrationBuilder.DropColumn(
                name: "modified_user_id",
                table: "tbl_point");

            migrationBuilder.DropColumn(
                name: "modified_user_id",
                table: "tbl_line");
        }
    }
}
