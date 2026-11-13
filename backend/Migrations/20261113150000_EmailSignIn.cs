using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PokemonTCG.API.Data;

#nullable disable

namespace PokemonTCG.API.Migrations;

/// <summary>
/// Email sign-in. A collector profile used to be one browser cookie; now a verified email
/// turns it into an account that can be signed in on any number of devices.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20261113150000_EmailSignIn")]
public sealed class EmailSignIn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- One row per signed-in browser. Existing device cookies keep working: their
            -- token hashes move here unchanged.
            CREATE TABLE IF NOT EXISTS user_sessions (
                token_hash varchar(64) PRIMARY KEY,
                user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
                created_at timestamptz NOT NULL,
                last_seen_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_user_sessions_user ON user_sessions(user_id);

            INSERT INTO user_sessions (token_hash, user_id, created_at, last_seen_at, expires_at)
            SELECT session_token_hash, id, created_at, last_seen_at, last_seen_at + interval '180 days'
            FROM app_users
            WHERE session_token_hash IS NOT NULL
            ON CONFLICT (token_hash) DO NOTHING;

            -- Kept (nullable) so the previous release still finds pre-migration sessions if it
            -- has to be rolled back. New code never reads or writes it.
            ALTER TABLE app_users ALTER COLUMN session_token_hash DROP NOT NULL;

            -- email may already hold an unverified address copied from Stripe Checkout.
            -- Only a verified address signs anyone in, and each is unique.
            ALTER TABLE app_users ADD COLUMN IF NOT EXISTS email_verified_at timestamptz NULL;
            ALTER TABLE app_users ADD COLUMN IF NOT EXISTS email_alerts boolean NOT NULL DEFAULT true;
            ALTER TABLE app_users ADD COLUMN IF NOT EXISTS unsubscribe_token uuid NOT NULL DEFAULT gen_random_uuid();
            CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_verified_email
                ON app_users (lower(email)) WHERE email_verified_at IS NOT NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_unsubscribe_token ON app_users (unsubscribe_token);

            -- Six-digit codes, stored only as salted hashes, single use, short-lived.
            CREATE TABLE IF NOT EXISTS sign_in_codes (
                id bigserial PRIMARY KEY,
                email varchar(320) NOT NULL,
                code_salt varchar(32) NOT NULL,
                code_hash varchar(64) NOT NULL,
                created_at timestamptz NOT NULL,
                expires_at timestamptz NOT NULL,
                attempts integer NOT NULL DEFAULT 0,
                consumed_at timestamptz NULL
            );
            CREATE INDEX IF NOT EXISTS ix_sign_in_codes_email_created ON sign_in_codes (email, created_at DESC);

            -- Set once an alert has been emailed, or deliberately not emailed. Alerts that
            -- already exist were shown in the app, so they are not sent as a backlog.
            ALTER TABLE alert_events ADD COLUMN IF NOT EXISTS email_processed_at timestamptz NULL;
            UPDATE alert_events SET email_processed_at = created_at WHERE email_processed_at IS NULL;
            CREATE INDEX IF NOT EXISTS ix_alert_events_email_pending
                ON alert_events (user_id, created_at) WHERE email_processed_at IS NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_alert_events_email_pending;
            ALTER TABLE alert_events DROP COLUMN IF EXISTS email_processed_at;

            DROP TABLE IF EXISTS sign_in_codes;

            DROP INDEX IF EXISTS ux_app_users_unsubscribe_token;
            DROP INDEX IF EXISTS ux_app_users_verified_email;
            ALTER TABLE app_users DROP COLUMN IF EXISTS unsubscribe_token;
            ALTER TABLE app_users DROP COLUMN IF EXISTS email_alerts;
            ALTER TABLE app_users DROP COLUMN IF EXISTS email_verified_at;

            -- Users created after the migration have no device token; give them a random,
            -- unusable one so the NOT NULL constraint can be restored.
            UPDATE app_users
            SET session_token_hash = encode(sha256(convert_to(id::text || clock_timestamp()::text, 'UTF8')), 'hex')
            WHERE session_token_hash IS NULL;
            ALTER TABLE app_users ALTER COLUMN session_token_hash SET NOT NULL;

            DROP TABLE IF EXISTS user_sessions;
            """);
    }
}
