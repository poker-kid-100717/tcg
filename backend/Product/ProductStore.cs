using Npgsql;
using NpgsqlTypes;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Product;

public sealed class ProductStore(string connectionString)
{
    private readonly string _connectionString = connectionString;

    public string ConnectionString => _connectionString;

    public sealed record UserRow(Guid Id, string? Email);
    public sealed record SubscriptionRow(
        Guid UserId,
        string? CustomerId,
        string? SubscriptionId,
        string Plan,
        string Status,
        DateTimeOffset? CurrentPeriodEnd,
        bool CancelAtPeriodEnd);

    public sealed record WatchTarget(
        long Id,
        Guid UserId,
        string CardId,
        string Variant,
        string CardName,
        decimal? BaselinePrice,
        decimal? TargetBelow,
        decimal? TargetAbove,
        decimal? MovePercent,
        bool AlertSleeper = false,
        bool AlertTrendingDown = false,
        bool AlertUnusualMove = false);

    /// <summary>A rule's last evaluated state: whether its condition held, and when it last fired.</summary>
    public sealed record RuleState(bool Active, DateTimeOffset? LastFiredAt);

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    public async Task<UserRow?> FindUserByTokenHashAsync(string hash, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT id, email FROM app_users WHERE session_token_hash = @hash", connection);
        command.Parameters.AddWithValue("hash", hash);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new UserRow(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : null;
    }

    public async Task<UserRow> CreateUserAsync(Guid id, string tokenHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO app_users (id, session_token_hash, created_at, last_seen_at)
            VALUES (@id, @hash, @now, @now)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("hash", tokenHash);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new UserRow(id, null);
    }

    public async Task TouchUserAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE app_users SET last_seen_at = @now WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetEmailAsync(Guid id, string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE app_users SET email = @email WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SubscriptionRow?> GetSubscriptionAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT user_id, stripe_customer_id, stripe_subscription_id, plan, status, current_period_end, cancel_at_period_end
            FROM subscriptions WHERE user_id = @user
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSubscription(reader) : null;
    }

    public async Task<Guid?> FindUserByExternalAsync(string? customerId, string? subscriptionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(customerId) && string.IsNullOrWhiteSpace(subscriptionId)) return null;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT user_id FROM subscriptions
            WHERE (@sub IS NOT NULL AND stripe_subscription_id = @sub)
               OR (@customer IS NOT NULL AND stripe_customer_id = @customer)
            LIMIT 1
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("sub", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(subscriptionId) ? DBNull.Value : subscriptionId });
        command.Parameters.Add(new NpgsqlParameter("customer", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(customerId) ? DBNull.Value : customerId });
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is Guid id ? id : null;
    }

    public async Task UpsertSubscriptionAsync(
        Guid userId,
        string? customerId,
        string? subscriptionId,
        string plan,
        string status,
        DateTimeOffset? currentPeriodEnd,
        bool cancelAtPeriodEnd,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        DateTimeOffset? stateAt = null)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // stateAt is when the state was true at Stripe. An older state never overwrites a newer one, so events that
        // arrive out of order can't roll a subscription back.
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO subscriptions
                (user_id, stripe_customer_id, stripe_subscription_id, plan, status, current_period_end, cancel_at_period_end, updated_at, last_event_at)
            VALUES
                (@user, @customer, @sub, @plan, @status, @period, @cancel, @now, @state_at)
            ON CONFLICT (user_id) DO UPDATE SET
                stripe_customer_id = COALESCE(EXCLUDED.stripe_customer_id, subscriptions.stripe_customer_id),
                stripe_subscription_id = COALESCE(EXCLUDED.stripe_subscription_id, subscriptions.stripe_subscription_id),
                plan = CASE WHEN EXCLUDED.plan = '' THEN subscriptions.plan ELSE EXCLUDED.plan END,
                status = EXCLUDED.status,
                current_period_end = COALESCE(EXCLUDED.current_period_end, subscriptions.current_period_end),
                cancel_at_period_end = EXCLUDED.cancel_at_period_end,
                updated_at = EXCLUDED.updated_at,
                last_event_at = COALESCE(EXCLUDED.last_event_at, subscriptions.last_event_at)
            WHERE subscriptions.last_event_at IS NULL OR EXCLUDED.last_event_at IS NULL OR EXCLUDED.last_event_at >= subscriptions.last_event_at
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("state_at", NpgsqlDbType.TimestampTz) { Value = stateAt is null ? DBNull.Value : stateAt.Value });
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.Add(new NpgsqlParameter("customer", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(customerId) ? DBNull.Value : customerId });
        command.Parameters.Add(new NpgsqlParameter("sub", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(subscriptionId) ? DBNull.Value : subscriptionId });
        command.Parameters.AddWithValue("plan", plan ?? "");
        command.Parameters.AddWithValue("status", status);
        command.Parameters.Add(new NpgsqlParameter("period", NpgsqlDbType.TimestampTz)
            { Value = currentPeriodEnd is null ? DBNull.Value : currentPeriodEnd.Value });
        command.Parameters.AddWithValue("cancel", cancelAtPeriodEnd);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Records a Stripe event id. False when it was already recorded (a duplicate delivery).</summary>
    public async Task<bool> TryRecordStripeEventAsync(string id, string type, DateTimeOffset createdAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO stripe_events (id, type, created_at, received_at) VALUES (@id, @type, @created, @now)
            ON CONFLICT (id) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("type", type.Length > 100 ? type[..100] : type);
        command.Parameters.AddWithValue("created", createdAt);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Forgets an event whose processing failed, so Stripe's retry is processed rather than skipped.</summary>
    public async Task ForgetStripeEventAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("DELETE FROM stripe_events WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WatchlistItemView>> GetWatchlistAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT w.id, w.card_id, w.variant, w.card_name, w.set_name, w.image_url,
                   w.baseline_price, p.market, w.target_below, w.target_above, w.move_percent,
                   w.enabled, w.created_at, w.alert_sleeper, w.alert_trending_down, w.alert_unusual_move
            FROM watchlist_items w
            LEFT JOIN LATERAL (
                SELECT s.market
                FROM price_snapshots s
                WHERE s.card_id = w.card_id AND s.variant = w.variant
                ORDER BY s.date DESC
                LIMIT 1
            ) p ON true
            WHERE w.user_id = @user
            ORDER BY w.created_at DESC
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<WatchlistItemView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new WatchlistItemView(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                Prices.VariantLabel(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DecimalOrNull(reader, 6),
                DecimalOrNull(reader, 7),
                DecimalOrNull(reader, 8),
                DecimalOrNull(reader, 9),
                DecimalOrNull(reader, 10),
                reader.GetBoolean(11),
                reader.GetFieldValue<DateTimeOffset>(12))
            {
                AlertSleeper = reader.GetBoolean(13),
                AlertTrendingDown = reader.GetBoolean(14),
                AlertUnusualMove = reader.GetBoolean(15),
            });
        }
        return items;
    }

    public async Task<int> CountWatchlistAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM watchlist_items WHERE user_id = @user", connection);
        command.Parameters.AddWithValue("user", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<long> AddWatchlistAsync(
        Guid userId,
        CreateWatchlistRequest request,
        decimal? baselinePrice,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO watchlist_items
                (user_id, card_id, variant, card_name, set_name, image_url, baseline_price,
                 target_below, target_above, move_percent, alert_sleeper, alert_trending_down, alert_unusual_move, enabled, created_at)
            VALUES
                (@user, @card, @variant, @name, @set, @image, @baseline,
                 @below, @above, @move, @sleeper, @trend, @unusual, true, @now)
            ON CONFLICT (user_id, card_id, variant) DO UPDATE SET
                card_name = EXCLUDED.card_name,
                set_name = EXCLUDED.set_name,
                image_url = EXCLUDED.image_url,
                baseline_price = COALESCE(watchlist_items.baseline_price, EXCLUDED.baseline_price),
                target_below = EXCLUDED.target_below,
                target_above = EXCLUDED.target_above,
                move_percent = EXCLUDED.move_percent,
                alert_sleeper = EXCLUDED.alert_sleeper,
                alert_trending_down = EXCLUDED.alert_trending_down,
                alert_unusual_move = EXCLUDED.alert_unusual_move,
                enabled = true
            RETURNING id
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("card", request.CardId);
        command.Parameters.AddWithValue("variant", request.Variant);
        command.Parameters.AddWithValue("name", request.CardName);
        command.Parameters.AddWithValue("set", request.SetName);
        command.Parameters.Add(new NpgsqlParameter("image", NpgsqlDbType.Text)
            { Value = string.IsNullOrWhiteSpace(request.ImageUrl) ? DBNull.Value : request.ImageUrl });
        AddDecimal(command, "baseline", baselinePrice);
        AddDecimal(command, "below", request.TargetBelow);
        AddDecimal(command, "above", request.TargetAbove);
        AddDecimal(command, "move", request.MovePercent);
        command.Parameters.AddWithValue("sleeper", request.AlertSleeper);
        command.Parameters.AddWithValue("trend", request.AlertTrendingDown);
        command.Parameters.AddWithValue("unusual", request.AlertUnusualMove);
        command.Parameters.AddWithValue("now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> UpdateWatchlistAsync(
        Guid userId,
        long id,
        UpdateWatchlistRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            UPDATE watchlist_items
            SET target_below = @below, target_above = @above, move_percent = @move, enabled = @enabled,
                alert_sleeper = @sleeper, alert_trending_down = @trend, alert_unusual_move = @unusual
            WHERE id = @id AND user_id = @user
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        AddDecimal(command, "below", request.TargetBelow);
        AddDecimal(command, "above", request.TargetAbove);
        AddDecimal(command, "move", request.MovePercent);
        command.Parameters.AddWithValue("enabled", request.Enabled);
        command.Parameters.AddWithValue("sleeper", request.AlertSleeper);
        command.Parameters.AddWithValue("trend", request.AlertTrendingDown);
        command.Parameters.AddWithValue("unusual", request.AlertUnusualMove);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> DeleteWatchlistAsync(Guid userId, long id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM watchlist_items WHERE id = @id AND user_id = @user", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<WatchTarget>> GetEnabledWatchTargetsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, user_id, card_id, variant, card_name, baseline_price, target_below, target_above, move_percent,
                   alert_sleeper, alert_trending_down, alert_unusual_move
            FROM watchlist_items WHERE enabled = true
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<WatchTarget>();
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new WatchTarget(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DecimalOrNull(reader, 5),
                DecimalOrNull(reader, 6),
                DecimalOrNull(reader, 7),
                DecimalOrNull(reader, 8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetBoolean(11)));
        }
        return items;
    }

    public async Task<Dictionary<(long Item, string Kind), RuleState>> GetRuleStatesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT watchlist_item_id, kind, condition_active, last_fired_at FROM alert_rules", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var states = new Dictionary<(long, string), RuleState>();
        while (await reader.ReadAsync(cancellationToken))
        {
            states[(reader.GetInt64(0), reader.GetString(1))] = new RuleState(reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
        }
        return states;
    }

    public async Task SetRuleStateAsync(long watchlistItemId, string kind, bool active, DateTimeOffset? firedAt, DateOnly evaluatedOn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO alert_rules (watchlist_item_id, kind, condition_active, last_fired_at, last_evaluated_on, updated_at)
            VALUES (@item, @kind, @active, @fired, @on, @now)
            ON CONFLICT (watchlist_item_id, kind) DO UPDATE SET
                condition_active = EXCLUDED.condition_active,
                last_fired_at = COALESCE(EXCLUDED.last_fired_at, alert_rules.last_fired_at),
                last_evaluated_on = EXCLUDED.last_evaluated_on,
                updated_at = EXCLUDED.updated_at
            """, connection);
        command.Parameters.AddWithValue("item", watchlistItemId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("active", active);
        command.Parameters.Add(new NpgsqlParameter("fired", NpgsqlDbType.TimestampTz) { Value = firedAt is null ? DBNull.Value : firedAt.Value });
        command.Parameters.AddWithValue("on", evaluatedOn);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> AddAlertIfMissingAsync(
        Guid userId,
        long watchlistItemId,
        string kind,
        string eventKey,
        string message,
        decimal? currentValue,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO alert_events
                (user_id, watchlist_item_id, kind, event_key, message, current_value, created_at)
            VALUES (@user, @item, @kind, @key, @message, @value, @now)
            ON CONFLICT (user_id, event_key) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("item", watchlistItemId);
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("key", eventKey);
        command.Parameters.AddWithValue("message", message);
        AddDecimal(command, "value", currentValue);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<IReadOnlyList<AlertView>> GetAlertsAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, watchlist_item_id, kind, message, current_value, created_at, read_at
            FROM alert_events
            WHERE user_id = @user
            ORDER BY created_at DESC
            LIMIT @limit
            """, connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var alerts = new List<AlertView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(new AlertView(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                DecimalOrNull(reader, 4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return alerts;
    }

    public async Task<bool> MarkAlertReadAsync(Guid userId, long id, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE alert_events SET read_at = COALESCE(read_at, @now) WHERE id = @id AND user_id = @user", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("now", now);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task MarkAllAlertsReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "UPDATE alert_events SET read_at = @now WHERE user_id = @user AND read_at IS NULL", connection);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SubscriptionRow ReadSubscription(NpgsqlDataReader reader) => new(
        reader.GetGuid(0),
        reader.IsDBNull(1) ? null : reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
        reader.GetBoolean(6));

    private static decimal? DecimalOrNull(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDecimal(ordinal);

    private static void AddDecimal(NpgsqlCommand command, string name, decimal? value) =>
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Numeric)
            { Value = value is null ? DBNull.Value : value.Value });
}
