using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSpalatorie.Migrations
{
    /// <inheritdoc />
    public partial class customerView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStarted",
                table: "DeliveryRoutes",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStarted",
                table: "DeliveryRoutes");
        }
    }
}
