using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokemonTCG.API.Migrations
{
    /// <inheritdoc />
    public partial class PriceGuide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "WishlistItems");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.CreateTable(
                name: "cards",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    set_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    set_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rarity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    image_small = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tcgplayer_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "snapshot_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    cards_seen = table.Column<int>(type: "integer", nullable: false),
                    prices_written = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_snapshot_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "price_snapshots",
                columns: table => new
                {
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    market = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    low = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    mid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    high = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_snapshots", x => new { x.card_id, x.variant, x.date });
                    table.ForeignKey(
                        name: "FK_price_snapshots_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cards_set_id",
                table: "cards",
                column: "set_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_snapshots_date",
                table: "price_snapshots",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "price_snapshots");

            migrationBuilder.DropTable(
                name: "snapshot_runs");

            migrationBuilder.DropTable(
                name: "cards");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Avatar = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Total = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WishlistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CardId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WishlistItems_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    CardId = table.Column<string>(type: "text", nullable: false),
                    CardImage = table.Column<string>(type: "text", nullable: false),
                    CardName = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId",
                table: "OrderItems",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UserId",
                table: "Orders",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_UserId",
                table: "WishlistItems",
                column: "UserId");
        }
    }
}
