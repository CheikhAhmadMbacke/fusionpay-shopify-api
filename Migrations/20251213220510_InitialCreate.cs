using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FusionPayProxy.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopifyOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    OrderNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FinancialStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FulfillmentStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CustomerEmail = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OrderData = table.Column<string>(type: "text", nullable: true),
                    WhatsAppSent = table.Column<bool>(type: "INTEGER", nullable: false),
                    WhatsAppSentAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopifyOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShopifyOrderId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ShopifyOrderNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    FusionPayToken = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CustomerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WebhookEvent = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsProcessed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RawWebhookData = table.Column<string>(type: "text", nullable: true),
                    TransactionNumber = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Fees = table.Column<decimal>(type: "decimal(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TokenPay = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDuplicate = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProcessingResult = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    HttpMethod = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyOrders_FinancialStatus",
                table: "ShopifyOrders",
                column: "FinancialStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyOrders_OrderId",
                table: "ShopifyOrders",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyOrders_OrderNumber",
                table: "ShopifyOrders",
                column: "OrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ShopifyOrders_WhatsAppSent",
                table: "ShopifyOrders",
                column: "WhatsAppSent");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CreatedAt",
                table: "Transactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_FusionPayToken",
                table: "Transactions",
                column: "FusionPayToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ShopifyOrderId",
                table: "Transactions",
                column: "ShopifyOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status",
                table: "Transactions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status_IsProcessed",
                table: "Transactions",
                columns: new[] { "Status", "IsProcessed" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_EventType",
                table: "WebhookLogs",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_IsDuplicate",
                table: "WebhookLogs",
                column: "IsDuplicate");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_ReceivedAt",
                table: "WebhookLogs",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookLogs_TokenPay",
                table: "WebhookLogs",
                column: "TokenPay");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopifyOrders");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "WebhookLogs");
        }
    }
}
