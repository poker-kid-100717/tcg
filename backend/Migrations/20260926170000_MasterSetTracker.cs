using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonTCG.API.Data;

#nullable disable

namespace PokemonTCG.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926170000_MasterSetTracker")]
public sealed class MasterSetTracker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS master_sets (
                id bigserial PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
                set_id varchar(64) NOT NULL,
                set_name varchar(200) NOT NULL,
                set_series varchar(200) NOT NULL,
                logo_url varchar(500) NULL,
                unique_cards integer NOT NULL,
                required_printings integer NOT NULL,
                created_at timestamptz NOT NULL,
                CONSTRAINT uq_master_set_user_set UNIQUE (user_id, set_id)
            );
            CREATE INDEX IF NOT EXISTS ix_master_sets_user ON master_sets(user_id, created_at DESC);

            CREATE TABLE IF NOT EXISTS master_set_items (
                master_set_id bigint NOT NULL REFERENCES master_sets(id) ON DELETE CASCADE,
                card_id varchar(64) NOT NULL,
                variant varchar(40) NOT NULL,
                card_name varchar(200) NOT NULL,
                card_number varchar(64) NOT NULL,
                rarity varchar(120) NULL,
                image_url varchar(500) NULL,
                tcgplayer_url varchar(500) NULL,
                initial_market_price numeric(12,2) NULL,
                owned_quantity integer NOT NULL DEFAULT 0,
                condition varchar(32) NULL,
                acquired_price numeric(12,2) NULL,
                updated_at timestamptz NOT NULL,
                PRIMARY KEY (master_set_id, card_id, variant),
                CONSTRAINT ck_master_set_items_owned_nonnegative CHECK (owned_quantity >= 0),
                CONSTRAINT ck_master_set_items_acquired_nonnegative CHECK (acquired_price IS NULL OR acquired_price >= 0)
            );
            CREATE INDEX IF NOT EXISTS ix_master_set_items_missing
                ON master_set_items(master_set_id, owned_quantity)
                WHERE owned_quantity = 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS master_set_items;
            DROP TABLE IF EXISTS master_sets;
            """);
    }
}
