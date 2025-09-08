using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateCRM.Migrations
{
    /// <inheritdoc />
    public partial class AddImagePathToProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Properties",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Properties");
        }
    }
}
