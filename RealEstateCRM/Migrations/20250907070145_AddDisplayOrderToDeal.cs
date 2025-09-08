using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateCRM.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayOrderToDeal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "Deals",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "Deals");
        }
    }
}
