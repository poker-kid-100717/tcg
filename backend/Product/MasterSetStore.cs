using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public sealed class MasterSetStore(string connectionString)
{
    public sealed record SeedItem(
        string CardId, string Variant, string CardName, string CardNumber, string? Rarity,
        string? ImageUrl, string? TcgplayerUrl, decimal? MarketPrice);

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    public async Task<long?> FindIdAsync(Guid userId, string setId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT id FROM master_sets WHERE user_id = @user AND set_id = @set", connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("set", setId);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    public async Task<int> CountAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM master_sets WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<long> CreateAsync(
        Guid userId, TcgSet set, IReadOnlyList<SeedItem> items, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        await using var setCommand = new NpgsqlCommand(
            """
            INSERT INTO master_sets
                (user_id, set_id, set_name, set_series, logo_url, unique_cards, required_printings, created_at)
            VALUES
                (@user, @set, @name, @series, @logo, @unique, @required, @now)
            ON CONFLICT (user_id, set_id) DO UPDATE SET
                set_name = EXCLUDED.set_name,
                set_series = EXCLUDED.set_series,
                logo_url = EXCLUDED.logo_url
            RETURNING id
            """, connection, transaction);
        setCommand.Parameters.AddWithValue("user", userId);
        setCommand.Parameters.AddWithValue("set", set.Id);
        setCommand.Parameters.AddWithValue("name", set.Name);
        setCommand.Parameters.AddWithValue("series", set.Series);
        setCommand.Parameters.Add(new NpgsqlParameter("logo", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(set.Images?.Logo) ? DBNull.Value : set.Images.Logo });
        setCommand.Parameters.AddWithValue("unique", items.Select(i => i.CardId).Distinct().Count());
        setCommand.Parameters.AddWithValue("required", items.Count);
        setCommand.Parameters.AddWithValue("now", now);
        var masterSetId = Convert.ToInt64(await setCommand.ExecuteScalarAsync(ct));

        foreach (var item in items)
        {
            await using var itemCommand = new NpgsqlCommand(
                """
                INSERT INTO master_set_items
                    (master_set_id, card_id, variant, card_name, card_number, rarity, image_url,
                     tcgplayer_url, initial_market_price, owned_quantity, updated_at)
                VALUES
                    (@master, @card, @variant, @name, @number, @rarity, @image,
                     @url, @market, 0, @now)
                ON CONFLICT (master_set_id, card_id, variant) DO UPDATE SET
                    card_name = EXCLUDED.card_name,
                    card_number = EXCLUDED.card_number,
                    rarity = EXCLUDED.rarity,
                    image_url = EXCLUDED.image_url,
                    tcgplayer_url = EXCLUDED.tcgplayer_url,
                    initial_market_price = COALESCE(EXCLUDED.initial_market_price, master_set_items.initial_market_price)
                """, connection, transaction);
            itemCommand.Parameters.AddWithValue("master", masterSetId);
            itemCommand.Parameters.AddWithValue("card", item.CardId);
            itemCommand.Parameters.AddWithValue("variant", item.Variant);
            itemCommand.Parameters.AddWithValue("name", item.CardName);
            itemCommand.Parameters.AddWithValue("number", item.CardNumber);
            AddText(itemCommand, "rarity", item.Rarity);
            AddText(itemCommand, "image", item.ImageUrl);
            AddText(itemCommand, "url", item.TcgplayerUrl);
            AddDecimal(itemCommand, "market", item.MarketPrice);
            itemCommand.Parameters.AddWithValue("now", now);
            await itemCommand.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
        return masterSetId;
    }

    public async Task<IReadOnlyList<MasterSetSummaryView>> GetSummariesAsync(Guid userId, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT m.id, m.set_id, m.set_name, m.set_series, m.logo_url, m.unique_cards, m.required_printings,
                   count(*) FILTER (WHERE i.owned_quantity > 0)::int AS owned,
                   COALESCE(sum(COALESCE(p.market, i.initial_market_price)) FILTER (WHERE i.owned_quantity > 0), 0) AS owned_value,
                   COALESCE(sum(COALESCE(p.market, i.initial_market_price)) FILTER (WHERE i.owned_quantity = 0), 0) AS missing_cost
            FROM master_sets m
            JOIN master_set_items i ON i.master_set_id = m.id
            LEFT JOIN LATERAL (
                SELECT s.market
                FROM price_snapshots s
                WHERE s.card_id = i.card_id AND s.variant = i.variant AND s.market IS NOT NULL
                ORDER BY s.date DESC LIMIT 1
            ) p ON true
            WHERE m.user_id = @user
            GROUP BY m.id
            ORDER BY m.created_at DESC
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<MasterSetSummaryView>();
        while (await reader.ReadAsync(ct))
        {
            var required = reader.GetInt32(6);
            var owned = reader.GetInt32(7);
            result.Add(new MasterSetSummaryView(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetInt32(5), required, owned,
                required == 0 ? 0 : Math.Round(owned * 100m / required, 1),
                reader.GetDecimal(8), reader.GetDecimal(9)));
        }
        return result;
    }

    public async Task<MasterSetDetailView?> GetDetailAsync(Guid userId, long id, CancellationToken ct)
    {
        var summaries = await GetSummariesAsync(userId, ct);
        var summary = summaries.SingleOrDefault(s => s.Id == id);
        if (summary is null) return null;

        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            SELECT i.card_id, i.variant, i.card_name, i.card_number, i.rarity, i.image_url, i.tcgplayer_url,
                   COALESCE(now.market, i.initial_market_price) AS current_market,
                   CASE WHEN old.market IS NOT NULL AND old.market > 0 AND now.market IS NOT NULL
                        THEN round(((now.market / old.market) - 1) * 100, 1) ELSE NULL END AS change_30,
                   i.owned_quantity, i.condition, i.acquired_price
            FROM master_set_items i
            JOIN master_sets m ON m.id = i.master_set_id
            LEFT JOIN LATERAL (
                SELECT s.market, s.date
                FROM price_snapshots s
                WHERE s.card_id = i.card_id AND s.variant = i.variant AND s.market IS NOT NULL
                ORDER BY s.date DESC LIMIT 1
            ) now ON true
            LEFT JOIN LATERAL (
                SELECT s.market
                FROM price_snapshots s
                WHERE s.card_id = i.card_id AND s.variant = i.variant AND s.market IS NOT NULL
                  AND s.date <= COALESCE(now.date, current_date) - 30
                ORDER BY s.date DESC LIMIT 1
            ) old ON true
            WHERE m.id = @id AND m.user_id = @user
            ORDER BY lpad(regexp_replace(i.card_number, '\\D', '', 'g'), 8, '0'), i.card_number, i.card_name, i.variant
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<MasterSetItemView>();
        while (await reader.ReadAsync(ct))
        {
            var market = DecimalOrNull(reader, 7);
            var change = DecimalOrNull(reader, 8);
            var owned = reader.GetInt32(9);
            var (signal, reason) = Signal(owned, market, change);
            items.Add(new MasterSetItemView(
                reader.GetString(0), reader.GetString(1), Prices.VariantLabel(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                market, change, owned, reader.IsDBNull(10) ? null : reader.GetString(10),
                DecimalOrNull(reader, 11), signal, reason));
        }
        return new MasterSetDetailView(summary, items);
    }

    public async Task<bool> UpdateItemAsync(
        Guid userId, long masterSetId, UpdateMasterSetItemRequest request, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            """
            UPDATE master_set_items i
            SET owned_quantity = @owned, condition = @condition, acquired_price = @price, updated_at = @now
            FROM master_sets m
            WHERE i.master_set_id = @master AND i.card_id = @card AND i.variant = @variant
              AND m.id = i.master_set_id AND m.user_id = @user
            """, connection);
        command.Parameters.AddWithValue("master", masterSetId);
        command.Parameters.AddWithValue("card", request.CardId);
        command.Parameters.AddWithValue("variant", request.Variant);
        command.Parameters.AddWithValue("owned", request.OwnedQuantity);
        AddText(command, "condition", request.Condition);
        AddDecimal(command, "price", request.AcquiredPrice);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    public async Task<bool> DeleteAsync(Guid userId, long id, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = new NpgsqlCommand(
            "DELETE FROM master_sets WHERE id = @id AND user_id = @user", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteNonQueryAsync(ct) == 1;
    }

    private static (string Signal, string Reason) Signal(int owned, decimal? market, decimal? change30)
    {
        if (owned > 0) return ("Owned", "Already in your master set.");
        if (market is null) return ("Research", "No reliable current market price is available.");
        if (change30 <= -8) return ("Watch", $"Price is down {Math.Abs(change30.Value):0.#}% over about 30 days; consider waiting for stabilization or a target.");
        if (change30 >= 8) return ("Watch", $"Price is up {change30.Value:0.#}% over about 30 days; avoid chasing a recent move.");
        return ("Buy candidate", change30 is null
            ? "Current price is available, but there is not enough 30-day history for a trend call."
            : $"Price has been relatively stable ({change30.Value:+0.#;-0.#;0}% over about 30 days).");
    }

    private static decimal? DecimalOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static void AddText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(value) ? DBNull.Value : value });

    private static void AddDecimal(NpgsqlCommand command, string name, decimal? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric)
            { Value = value is null ? DBNull.Value : value.Value });
}
