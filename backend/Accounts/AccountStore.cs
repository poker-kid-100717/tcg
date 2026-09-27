using Npgsql;
using NpgsqlTypes;

namespace PokemonTCG.API.Accounts;

/// <summary>Account rows (app_users), in the same raw-SQL style as the product store.</summary>
public sealed class AccountStore(string connectionString)
{
    public sealed record AccountRow(Guid Id, string? Email, string? DisplayName, DateTimeOffset CreatedAt);

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<AccountRow?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id, email, display_name, created_at FROM app_users WHERE id = @id AND auth_subject IS NOT NULL", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    /// <summary>
    /// The account for an identity, created on first sign-in. A pre-existing anonymous profile (identified by the hash of
    /// its old device cookie) that isn't linked to anyone yet becomes this account, keeping its watchlist and master sets.
    /// </summary>
    public async Task<Guid> SignInAsync(string issuer, string subject, string? email, string? displayName, string? legacyTokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var find = new NpgsqlCommand("SELECT id FROM app_users WHERE auth_issuer = @iss AND auth_subject = @sub FOR UPDATE", connection, transaction))
        {
            find.Parameters.AddWithValue("iss", issuer);
            find.Parameters.AddWithValue("sub", subject);
            if (await find.ExecuteScalarAsync(cancellationToken) is Guid existing)
            {
                await UpdateProfileAsync(connection, transaction, existing, email, displayName, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return existing;
            }
        }

        if (!string.IsNullOrWhiteSpace(legacyTokenHash))
        {
            await using var link = new NpgsqlCommand(
                """
                UPDATE app_users SET auth_issuer = @iss, auth_subject = @sub, session_token_hash = NULL
                WHERE session_token_hash = @hash AND auth_subject IS NULL
                RETURNING id
                """, connection, transaction);
            link.Parameters.AddWithValue("iss", issuer);
            link.Parameters.AddWithValue("sub", subject);
            link.Parameters.AddWithValue("hash", legacyTokenHash);
            if (await link.ExecuteScalarAsync(cancellationToken) is Guid linked)
            {
                await UpdateProfileAsync(connection, transaction, linked, email, displayName, now, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return linked;
            }
        }

        var id = Guid.CreateVersion7();
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO app_users (id, auth_issuer, auth_subject, email, display_name, created_at, last_seen_at)
            VALUES (@id, @iss, @sub, @email, @name, @now, @now)
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("id", id);
            insert.Parameters.AddWithValue("iss", issuer);
            insert.Parameters.AddWithValue("sub", subject);
            insert.Parameters.Add(Text("email", Clip(email, 320)));
            insert.Parameters.Add(Text("name", Clip(displayName, 200)));
            insert.Parameters.AddWithValue("now", now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>Deletes the account; its subscription row, watchlist, alerts, master sets and preferences cascade with it.</summary>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM app_users WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static async Task UpdateProfileAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, string? email, string? displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand(
            """
            UPDATE app_users SET email = COALESCE(@email, email), display_name = COALESCE(@name, display_name), last_seen_at = @now
            WHERE id = @id
            """, connection, transaction);
        update.Parameters.AddWithValue("id", id);
        update.Parameters.Add(Text("email", Clip(email, 320)));
        update.Parameters.Add(Text("name", Clip(displayName, 200)));
        update.Parameters.AddWithValue("now", now);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static AccountRow Read(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3));

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = (object?)value ?? DBNull.Value };

    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : value.Trim()[..max];
}
