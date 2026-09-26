using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokemonTCG.API.Migrations
{
    /// <inheritdoc />
    public partial class CatalogAndProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "price_snapshots",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PokemonTcg");

            migrationBuilder.AddColumn<string>(
                name: "flavor_text",
                table: "cards",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hp",
                table: "cards",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_large",
                table: "cards",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tcgplayer_product_id",
                table: "cards",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "types",
                table: "cards",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_protection_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "latest_prices",
                columns: table => new
                {
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    market = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    low = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    mid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    high = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    updated_on = table.Column<DateOnly>(type: "date", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "PokemonTcg")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_latest_prices", x => new { x.card_id, x.variant });
                    table.ForeignKey(
                        name: "FK_latest_prices_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_observations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    grade = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    grading_company = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    shipping = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_observations", x => x.id);
                    table.CheckConstraint("ck_market_observations_price", "price >= 0");
                    table.ForeignKey(
                        name: "FK_market_observations_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_signals",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    card_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    lookback_days = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_signals", x => x.id);
                    table.ForeignKey(
                        name: "FK_market_signals_cards_card_id",
                        column: x => x.card_id,
                        principalTable: "cards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sets",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    series = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    printed_total = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    symbol_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tcgplayer_group_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_observations_card_id_variant_kind_observed_at",
                table: "market_observations",
                columns: new[] { "card_id", "variant", "kind", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_market_observations_dedupe_key",
                table: "market_observations",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_market_signals_as_of_kind",
                table: "market_signals",
                columns: new[] { "as_of", "kind" });

            migrationBuilder.CreateIndex(
                name: "IX_market_signals_card_id_variant_as_of_kind",
                table: "market_signals",
                columns: new[] { "card_id", "variant", "as_of", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "latest_prices");

            migrationBuilder.DropTable(
                name: "market_observations");

            migrationBuilder.DropTable(
                name: "market_signals");

            migrationBuilder.DropTable(
                name: "sets");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "price_snapshots");

            migrationBuilder.DropColumn(
                name: "flavor_text",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "hp",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "image_large",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "tcgplayer_product_id",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "types",
                table: "cards");
        }
    }
}
