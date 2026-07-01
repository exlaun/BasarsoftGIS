using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddColorColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "tbl_polygon",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "tbl_point",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "tbl_line",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "tbl_polygon");

            migrationBuilder.DropColumn(
                name: "color",
                table: "tbl_point");

            migrationBuilder.DropColumn(
                name: "color",
                table: "tbl_line");
        }
    }
}
