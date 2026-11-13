using Npgsql;
using NpgsqlTypes;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// One sealed product with today's market price next to its MSRP. <see cref="MsrpSource"/> says where the
/// MSRP came from: "listed" (curated for this exact product) or "typical" (the usual price for its type in
/// the current era). Null MSRP means we don't know it, and the page says so instead of guessing.
/// </summary>
public record SealedProductView(
    int ProductId,
    string Name,
    string SetName,
    string? SetId,
    string ProductType,
    string? ImageUrl,
    string? TcgplayerUrl,
    DateOnly? ReleasedOn,
    decimal? Msrp,
    string? MsrpSource,
    decimal? Market,
    decimal? Low,
    DateOnly? AsOf,
    decimal? Change30Percent,
    decimal? PercentOverMsrp);

public record SealedListView(DateOnly? AsOf, IReadOnlyList<string> Types, IReadOnlyList<SealedProductView> Products);

public sealed class SealedStore(string connectionString)
{
    public async Task<SealedListView> ListAsync(
        string? type,
        string? search,
        bool currentEraOnly,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var types = new List<string>();
        await using (var typesCommand = new NpgsqlCommand(
            "SELECT DISTINCT product_type FROM sealed_products ORDER BY product_type", connection))
        await using (var reader = await typesCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken)) types.Add(reader.GetString(0));
        }

        await using var command = new NpgsqlCommand(
            """
            WITH latest AS (
                SELECT DISTINCT ON (product_id) product_id, date, market, low
                FROM sealed_prices
                ORDER BY product_id, date DESC
            ),
            month_ago AS (
                SELECT DISTINCT ON (product_id) product_id, market
                FROM sealed_prices
                WHERE date <= @month_ago
                ORDER BY product_id, date DESC
            )
            SELECT p.product_id, p.name, p.group_name, p.set_id, p.product_type, p.image_url, p.tcgplayer_url,
                   p.released_on, p.msrp, l.date, l.market, l.low, m.market
            FROM sealed_products p
            LEFT JOIN latest l ON l.product_id = p.product_id
            LEFT JOIN month_ago m ON m.product_id = p.product_id
            WHERE (@type IS NULL OR p.product_type = @type)
              AND (@search IS NULL OR p.name ILIKE '%' || @search || '%' OR p.group_name ILIKE '%' || @search || '%')
              AND (NOT @current_era OR p.released_on >= @era_start)
            ORDER BY p.released_on DESC NULLS LAST, p.group_name, p.name
            LIMIT 300
            """, connection);
        command.Parameters.Add(new NpgsqlParameter("month_ago", NpgsqlDbType.Date) { Value = today.AddDays(-30) });
        command.Parameters.Add(new NpgsqlParameter("type", NpgsqlDbType.Text) { Value = (object?)NullIfBlank(type) ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("search", NpgsqlDbType.Text) { Value = (object?)EscapeLike(NullIfBlank(search)) ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("current_era", NpgsqlDbType.Boolean) { Value = currentEraOnly });
        command.Parameters.Add(new NpgsqlParameter("era_start", NpgsqlDbType.Date) { Value = SealedProducts.TypicalMsrpEraStart });

        var products = new List<SealedProductView>();
        DateOnly? asOf = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var productType = reader.GetString(4);
                DateOnly? released = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7);
                decimal? listed = reader.IsDBNull(8) ? null : reader.GetDecimal(8);
                var typical = SealedProducts.TypicalMsrp(productType, released);
                var msrp = listed ?? typical;
                DateOnly? date = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9);
                decimal? market = reader.IsDBNull(10) ? null : reader.GetDecimal(10);
                decimal? monthAgo = reader.IsDBNull(12) ? null : reader.GetDecimal(12);
                if (date is { } d && (asOf is null || d > asOf)) asOf = d;

                products.Add(new SealedProductView(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    productType,
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    released,
                    msrp,
                    listed is not null ? "listed" : typical is not null ? "typical" : null,
                    market,
                    reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                    date,
                    Percent(market, monthAgo),
                    Percent(market, msrp)));
            }
        }

        return new SealedListView(asOf, types, products);
    }

    /// <summary>How far <paramref name="value"/> is above (+) or below (−) <paramref name="reference"/>, in percent.</summary>
    private static decimal? Percent(decimal? value, decimal? reference) =>
        value is { } v && reference is { } r && r > 0 ? Math.Round((v / r - 1m) * 100m, 1) : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Search text is matched literally: LIKE wildcards in it are escaped.</summary>
    private static string? EscapeLike(string? value) =>
        value?.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

public static class SealedEndpoints
{
    public static void MapSealed(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sealed", (string? type, string? q, bool? all, SealedStore store, TimeProvider clock, CancellationToken ct) =>
            store.ListAsync(type, q, currentEraOnly: all != true,
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct));
    }
}
