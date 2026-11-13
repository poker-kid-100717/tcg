using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace PokemonTCG.API.Product;

/// <summary>
/// A collector profile. <see cref="Email"/> is set only once the address has been verified with a
/// sign-in code; until then the profile lives in one browser and <see cref="SignedIn"/> is false.
/// </summary>
public sealed record AppUser(Guid Id, string? Email, bool EmailAlerts)
{
    public bool SignedIn => Email is not null;
}

public sealed record SessionRecord(AppUser User, DateTimeOffset ExpiresAt);

/// <summary>Users, browser sessions and sign-in codes.</summary>
public sealed class AccountStore(string connectionString, ILogger<AccountStore> logger)
{
    /// <summary>Wrong guesses allowed per code before it stops working.</summary>
    public const int MaxCodeAttempts = 5;

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    // ---- Sessions ---------------------------------------------------------------------------

    public async Task<SessionRecord?> FindSessionAsync(string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT u.id,
                   CASE WHEN u.email_verified_at IS NOT NULL THEN u.email END,
                   u.email_alerts,
                   s.expires_at
            FROM user_sessions s
            JOIN app_users u ON u.id = s.user_id
            WHERE s.token_hash = @hash AND s.expires_at > @now
            """, connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new SessionRecord(ReadUser(reader), reader.GetFieldValue<DateTimeOffset>(3));
    }

    /// <summary>Records activity, and pushes the expiry out when <paramref name="newExpiry"/> is given.</summary>
    public async Task TouchSessionAsync(
        string tokenHash,
        Guid userId,
        DateTimeOffset now,
        DateTimeOffset? newExpiry,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE user_sessions
            SET last_seen_at = @now, expires_at = COALESCE(@expires, expires_at)
            WHERE token_hash = @hash;
            UPDATE app_users SET last_seen_at = @now WHERE id = @user;
            """, connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.Add(new NpgsqlParameter("expires", NpgsqlDbType.TimestampTz)
            { Value = newExpiry is null ? DBNull.Value : newExpiry.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A new, anonymous profile for a browser that has no session yet.</summary>
    public async Task<AppUser> CreateAnonymousUserAsync(
        string tokenHash,
        DateTimeOffset now,
        DateTimeOffset expires,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app_users (id, created_at, last_seen_at) VALUES (@id, @now, @now);
            INSERT INTO user_sessions (token_hash, user_id, created_at, last_seen_at, expires_at)
            VALUES (@hash, @id, @now, @now, @expires);
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("expires", expires);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new AppUser(id, null, true);
    }

    public async Task DeleteSessionAsync(string tokenHash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM user_sessions WHERE token_hash = @hash", connection);
        command.Parameters.AddWithValue("hash", tokenHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---- Preferences ------------------------------------------------------------------------

    public async Task SetEmailAlertsAsync(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE app_users SET email_alerts = @enabled WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", userId);
        command.Parameters.AddWithValue("enabled", enabled);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <returns>False when the token matches no account.</returns>
    public async Task<bool> UnsubscribeAsync(Guid token, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE app_users SET email_alerts = false WHERE unsubscribe_token = @token", connection);
        command.Parameters.AddWithValue("token", token);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<Guid?> GetUnsubscribeTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT unsubscribe_token FROM app_users WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", userId);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid token ? token : null;
    }

    // ---- Sign-in codes ----------------------------------------------------------------------

    public async Task<int> CountCodesSinceAsync(string email, DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM sign_in_codes WHERE email = @email AND created_at > @since", connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("since", since);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    /// <summary>Stores a new code. Any earlier unused code for the address stops working.</summary>
    public async Task CreateCodeAsync(
        string email,
        string code,
        DateTimeOffset now,
        DateTimeOffset expires,
        CancellationToken cancellationToken)
    {
        var salt = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE sign_in_codes SET consumed_at = @now WHERE email = @email AND consumed_at IS NULL;
            INSERT INTO sign_in_codes (email, code_salt, code_hash, created_at, expires_at)
            VALUES (@email, @salt, @hash, @now, @expires);
            """, connection);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("salt", salt);
        command.Parameters.AddWithValue("hash", HashCode(salt, code));
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("expires", expires);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Checks a code and, if it is right, signs the browser in: in one transaction it consumes the code,
    /// finds or creates the account for the address, moves an anonymous profile's watchlist, master sets
    /// and subscription into it, and replaces the browser's session.
    /// </summary>
    /// <param name="current">The browser's profile before sign-in, if it had one.</param>
    /// <param name="currentTokenHash">The browser's session to replace, if it had one.</param>
    /// <returns>The signed-in account, or null when the code is wrong, used, expired or locked.</returns>
    public async Task<AppUser?> CompleteSignInAsync(
        string email,
        string code,
        AppUser? current,
        string? currentTokenHash,
        string newTokenHash,
        DateTimeOffset now,
        DateTimeOffset sessionExpires,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (!await ConsumeCodeAsync(connection, transaction, email, code, now, cancellationToken))
        {
            // Commit, not roll back: a wrong guess must still count against the code.
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var accountId = await FindVerifiedUserAsync(connection, transaction, email, cancellationToken);
        Guid? anonymousId = current is not null && !current.SignedIn ? current.Id : null;
        Guid targetId;
        if (accountId is not null)
        {
            targetId = accountId.Value;
            if (anonymousId is not null && anonymousId.Value != targetId)
                await MergeAsync(connection, transaction, anonymousId.Value, targetId, cancellationToken);
        }
        else if (anonymousId is not null)
        {
            // First sign-in for this address: the browser's profile becomes the account.
            targetId = anonymousId.Value;
            await ExecuteAsync(connection, transaction,
                "UPDATE app_users SET email = @email, email_verified_at = @now WHERE id = @id",
                cancellationToken, ("email", email), ("now", now), ("id", targetId));
        }
        else
        {
            // No browser profile, or signed in as someone else: start a fresh account.
            targetId = Guid.NewGuid();
            await ExecuteAsync(connection, transaction,
                """
                INSERT INTO app_users (id, email, email_verified_at, created_at, last_seen_at)
                VALUES (@id, @email, @now, @now, @now)
                """,
                cancellationToken, ("id", targetId), ("email", email), ("now", now));
        }

        if (currentTokenHash is not null)
            await ExecuteAsync(connection, transaction,
                "DELETE FROM user_sessions WHERE token_hash = @hash",
                cancellationToken, ("hash", currentTokenHash));

        // A new session token on sign-in, so a token issued before it can't be fixed onto the account.
        await ExecuteAsync(connection, transaction,
            """
            INSERT INTO user_sessions (token_hash, user_id, created_at, last_seen_at, expires_at)
            VALUES (@hash, @user, @now, @now, @expires)
            """,
            cancellationToken, ("hash", newTokenHash), ("user", targetId), ("now", now), ("expires", sessionExpires));

        AppUser user;
        await using (var select = new NpgsqlCommand(
            "SELECT id, email, email_alerts FROM app_users WHERE id = @id", connection, transaction))
        {
            select.Parameters.AddWithValue("id", targetId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            user = ReadUser(reader);
        }

        await transaction.CommitAsync(cancellationToken);
        return user;
    }

    public static string HashCode(string salt, string code) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));

    private static async Task<bool> ConsumeCodeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string email,
        string code,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long id;
        string salt, hash;
        int attempts;
        await using (var select = new NpgsqlCommand(
            """
            SELECT id, code_salt, code_hash, attempts FROM sign_in_codes
            WHERE email = @email AND consumed_at IS NULL AND expires_at > @now
            ORDER BY created_at DESC
            LIMIT 1
            FOR UPDATE
            """, connection, transaction))
        {
            select.Parameters.AddWithValue("email", email);
            select.Parameters.AddWithValue("now", now);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return false;
            id = reader.GetInt64(0);
            salt = reader.GetString(1);
            hash = reader.GetString(2);
            attempts = reader.GetInt32(3);
        }

        var expected = Convert.FromHexString(hash);
        var actual = Convert.FromHexString(HashCode(salt, code));
        if (attempts < MaxCodeAttempts && CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            await ExecuteAsync(connection, transaction,
                "UPDATE sign_in_codes SET consumed_at = @now WHERE id = @id",
                cancellationToken, ("now", now), ("id", id));
            return true;
        }

        await ExecuteAsync(connection, transaction,
            """
            UPDATE sign_in_codes
            SET attempts = attempts + 1,
                consumed_at = CASE WHEN attempts + 1 >= @max THEN @now ELSE consumed_at END
            WHERE id = @id
            """,
            cancellationToken, ("max", MaxCodeAttempts), ("now", now), ("id", id));
        return false;
    }

    private static async Task<Guid?> FindVerifiedUserAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string email,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT id FROM app_users WHERE lower(email) = @email AND email_verified_at IS NOT NULL FOR UPDATE",
            connection, transaction);
        command.Parameters.AddWithValue("email", email);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid id ? id : null;
    }

    /// <summary>
    /// Moves an anonymous profile into an account. Where both track the same printing or set, the
    /// account's copy wins. The anonymous profile is then deleted, unless both have live subscriptions:
    /// it is kept so its Stripe subscription still resolves to a user, and a warning is logged.
    /// </summary>
    private async Task MergeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid from,
        Guid to,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            """
            UPDATE watchlist_items w SET user_id = @to
            WHERE w.user_id = @from
              AND NOT EXISTS (
                  SELECT 1 FROM watchlist_items t
                  WHERE t.user_id = @to AND t.card_id = w.card_id AND t.variant = w.variant);

            UPDATE alert_events a SET user_id = @to
            WHERE a.user_id = @from
              AND a.watchlist_item_id IN (SELECT id FROM watchlist_items WHERE user_id = @to);

            UPDATE master_sets m SET user_id = @to
            WHERE m.user_id = @from
              AND NOT EXISTS (SELECT 1 FROM master_sets t WHERE t.user_id = @to AND t.set_id = m.set_id);
            """,
            cancellationToken, ("from", from), ("to", to));

        string? fromStatus = null, toStatus = null;
        bool fromHasRow = false, toHasRow = false;
        await using (var select = new NpgsqlCommand(
            "SELECT user_id, status FROM subscriptions WHERE user_id = @from OR user_id = @to",
            connection, transaction))
        {
            select.Parameters.AddWithValue("from", from);
            select.Parameters.AddWithValue("to", to);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetGuid(0) == from) { fromHasRow = true; fromStatus = reader.GetString(1); }
                else { toHasRow = true; toStatus = reader.GetString(1); }
            }
        }

        var fromLive = IsLive(fromStatus);
        var toLive = IsLive(toStatus);
        if (fromHasRow && (!toHasRow || (fromLive && !toLive)))
        {
            await ExecuteAsync(connection, transaction,
                """
                DELETE FROM subscriptions WHERE user_id = @to;
                UPDATE subscriptions SET user_id = @to WHERE user_id = @from;
                """,
                cancellationToken, ("from", from), ("to", to));
        }
        else if (fromLive && toLive)
        {
            logger.LogWarning(
                "Profile {From} and account {To} both have live subscriptions; the profile was kept so its billing still resolves",
                from, to);
            return;
        }

        await ExecuteAsync(connection, transaction,
            "DELETE FROM app_users WHERE id = @from",
            cancellationToken, ("from", from));
    }

    private static bool IsLive(string? status) => status is "active" or "trialing" or "past_due";

    private static AppUser ReadUser(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetBoolean(2));

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
