using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionPayProxy.Migrations
{
    /// <inheritdoc />
    public partial class NewProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerEmail",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnUrl",
                table: "Transactions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomerEmail",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReturnUrl",
                table: "Transactions");
        }
    }
}
