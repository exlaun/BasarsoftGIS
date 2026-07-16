using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Basarsoft.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPoiCategoryColor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "tbl_poi_category",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "color",
                table: "tbl_poi_category");
        }
    }
}
