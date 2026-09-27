using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonTCG.API.Data;

#nullable disable

namespace PokemonTCG.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926143000_TcgSignalMvp")]
public sealed class TcgSignalMvp : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS app_users (
                id uuid PRIMARY KEY,
                session_token_hash varchar(64) NOT NULL UNIQUE,
                email varchar(320) NULL,
                created_at timestamptz NOT NULL,
                last_seen_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS subscriptions (
                user_id uuid PRIMARY KEY REFERENCES app_users(id) ON DELETE CASCADE,
                stripe_customer_id varchar(128) NULL UNIQUE,
                stripe_subscription_id varchar(128) NULL UNIQUE,
                plan varchar(32) NOT NULL DEFAULT 'free',
                status varchar(32) NOT NULL DEFAULT 'inactive',
                current_period_end timestamptz NULL,
                cancel_at_period_end boolean NOT NULL DEFAULT false,
                updated_at timestamptz NOT NULL
            );

            CREATE TABLE IF NOT EXISTS watchlist_items (
                id bigserial PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
                card_id varchar(64) NOT NULL,
                variant varchar(40) NOT NULL,
                card_name varchar(200) NOT NULL,
                set_name varchar(200) NOT NULL,
                image_url varchar(500) NULL,
                baseline_price numeric(12,2) NULL,
                target_below numeric(12,2) NULL,
                target_above numeric(12,2) NULL,
                move_percent numeric(8,2) NULL,
                enabled boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL,
                CONSTRAINT uq_watchlist_user_card_variant UNIQUE (user_id, card_id, variant)
            );
            CREATE INDEX IF NOT EXISTS ix_watchlist_user ON watchlist_items(user_id);
            CREATE INDEX IF NOT EXISTS ix_watchlist_enabled ON watchlist_items(enabled) WHERE enabled = true;

            CREATE TABLE IF NOT EXISTS alert_events (
                id bigserial PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
                watchlist_item_id bigint NOT NULL REFERENCES watchlist_items(id) ON DELETE CASCADE,
                kind varchar(32) NOT NULL,
                event_key varchar(220) NOT NULL,
                message varchar(500) NOT NULL,
                current_value numeric(12,2) NULL,
                created_at timestamptz NOT NULL,
                read_at timestamptz NULL,
                CONSTRAINT uq_alert_user_key UNIQUE (user_id, event_key)
            );
            CREATE INDEX IF NOT EXISTS ix_alert_user_created ON alert_events(user_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_alert_user_unread ON alert_events(user_id, read_at) WHERE read_at IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS alert_events;
            DROP TABLE IF EXISTS watchlist_items;
            DROP TABLE IF EXISTS subscriptions;
            DROP TABLE IF EXISTS app_users;
            """);
    }
}
