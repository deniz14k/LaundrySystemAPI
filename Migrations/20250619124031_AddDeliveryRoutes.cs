using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiSpalatorie.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAssignedToRoute",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeliveryRoutes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryRouteOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    DeliveryRouteId = table.Column<int>(type: "int", nullable: false),
                    StopIndex = table.Column<int>(type: "int", nullable: false),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryRouteOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeliveryRouteOrders_DeliveryRoutes_DeliveryRouteId",
                        column: x => x.DeliveryRouteId,
                        principalTable: "DeliveryRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryRouteOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteOrders_DeliveryRouteId",
                table: "DeliveryRouteOrders",
                column: "DeliveryRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryRouteOrders_OrderId",
                table: "DeliveryRouteOrders",
                column: "OrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliveryRouteOrders");

            migrationBuilder.DropTable(
                name: "DeliveryRoutes");

            migrationBuilder.DropColumn(
                name: "IsAssignedToRoute",
                table: "Orders");
        }
    }
}
