using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonTCG.API.Data;

#nullable disable

namespace PokemonTCG.API.Migrations;

/// <summary>Sealed product (ETBs, booster bundles, boxes, tins) and its daily TCGplayer market price.</summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261114120000_SealedProductPrices")]
public sealed class SealedProductPrices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS sealed_products (
                product_id integer PRIMARY KEY,          -- TCGplayer product id
                group_id integer NOT NULL,               -- TCGplayer group (set)
                group_name varchar(200) NOT NULL,
                set_id varchar(64) NULL,                 -- the card database's set, when the group maps to one
                name varchar(300) NOT NULL,
                product_type varchar(40) NOT NULL,
                image_url varchar(500) NULL,
                tcgplayer_url varchar(500) NULL,
                released_on date NULL,
                msrp numeric(10,2) NULL,                 -- curated by hand; null falls back to the typical MSRP for its type
                updated_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sealed_products_type ON sealed_products(product_type);
            CREATE INDEX IF NOT EXISTS ix_sealed_products_released ON sealed_products(released_on DESC);

            CREATE TABLE IF NOT EXISTS sealed_prices (
                product_id integer NOT NULL REFERENCES sealed_products(product_id) ON DELETE CASCADE,
                date date NOT NULL,
                market numeric(12,2) NULL,
                low numeric(12,2) NULL,
                mid numeric(12,2) NULL,
                high numeric(12,2) NULL,
                PRIMARY KEY (product_id, date)
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS sealed_prices;
            DROP TABLE IF EXISTS sealed_products;
            """);
    }
}
