using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonTCG.API.Data;

#nullable disable

namespace PokemonTCG.API.Migrations;

/// <summary>
/// Real accounts and verified billing for TCG Signal:
/// app users are identified by their OpenID Connect issuer and subject (the old device-session token stays only so a
/// pre-existing anonymous profile can be linked on first sign-in); Stripe events are recorded for idempotency and
/// ordering; watchlist items gain signal alerts, with per-rule state for transitions and cooldowns; and each user can
/// keep preferences.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20260927100000_SignalAccounts")]
public sealed class SignalAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE app_users ALTER COLUMN session_token_hash DROP NOT NULL;
            ALTER TABLE app_users
                ADD COLUMN IF NOT EXISTS auth_issuer varchar(300) NULL,
                ADD COLUMN IF NOT EXISTS auth_subject varchar(300) NULL,
                ADD COLUMN IF NOT EXISTS display_name varchar(200) NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS uq_app_users_identity ON app_users(auth_issuer, auth_subject) WHERE auth_subject IS NOT NULL;

            ALTER TABLE subscriptions ADD COLUMN IF NOT EXISTS last_event_at timestamptz NULL;

            CREATE TABLE IF NOT EXISTS stripe_events (
                id varchar(255) PRIMARY KEY,
                type varchar(100) NOT NULL,
                created_at timestamptz NOT NULL,
                received_at timestamptz NOT NULL
            );

            ALTER TABLE watchlist_items
                ADD COLUMN IF NOT EXISTS alert_sleeper boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS alert_trending_down boolean NOT NULL DEFAULT false,
                ADD COLUMN IF NOT EXISTS alert_unusual_move boolean NOT NULL DEFAULT false;
            ALTER TABLE watchlist_items ADD CONSTRAINT ck_watchlist_move_percent CHECK (move_percent IS NULL OR (move_percent >= 1 AND move_percent <= 500));

            CREATE TABLE IF NOT EXISTS alert_rules (
                watchlist_item_id bigint NOT NULL REFERENCES watchlist_items(id) ON DELETE CASCADE,
                kind varchar(32) NOT NULL,
                condition_active boolean NOT NULL DEFAULT false,
                last_fired_at timestamptz NULL,
                last_evaluated_on date NULL,
                updated_at timestamptz NOT NULL,
                PRIMARY KEY (watchlist_item_id, kind)
            );

            CREATE TABLE IF NOT EXISTS user_preferences (
                user_id uuid PRIMARY KEY REFERENCES app_users(id) ON DELETE CASCADE,
                default_platform varchar(40) NULL,
                email_alerts boolean NOT NULL DEFAULT false,
                updated_at timestamptz NOT NULL
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS user_preferences;
            DROP TABLE IF EXISTS alert_rules;
            ALTER TABLE watchlist_items DROP CONSTRAINT IF EXISTS ck_watchlist_move_percent;
            ALTER TABLE watchlist_items DROP COLUMN IF EXISTS alert_unusual_move, DROP COLUMN IF EXISTS alert_trending_down, DROP COLUMN IF EXISTS alert_sleeper;
            DROP TABLE IF EXISTS stripe_events;
            ALTER TABLE subscriptions DROP COLUMN IF EXISTS last_event_at;
            DROP INDEX IF EXISTS uq_app_users_identity;
            ALTER TABLE app_users DROP COLUMN IF EXISTS display_name, DROP COLUMN IF EXISTS auth_subject, DROP COLUMN IF EXISTS auth_issuer;
            """);
    }
}
