using Npgsql;
using NpgsqlTypes;
using TcgSignal.Inventory.Domain;

namespace TcgSignal.Inventory.Infrastructure;

/// <summary>
/// Inventory.Service owns these tables even though the first deployment shares the existing Postgres cluster.
/// No user latitude/longitude is persisted; only public store coordinates and retailer observations are stored.
/// </summary>
public sealed class InventoryStore(string connectionString)
{
    private readonly string _connectionString = connectionString;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS inventory_observations (
                id bigserial PRIMARY KEY,
                observation_key varchar(220) NOT NULL UNIQUE,
                provider varchar(64) NOT NULL,
                retailer varchar(100) NOT NULL,
                store_id varchar(100) NOT NULL,
                store_name varchar(200) NOT NULL,
                address varchar(300) NOT NULL,
                city varchar(150) NOT NULL,
                region varchar(80) NOT NULL,
                postal_code varchar(20) NOT NULL,
                latitude double precision NOT NULL,
                longitude double precision NOT NULL,
                product_sku varchar(100) NOT NULL,
                product_name varchar(300) NOT NULL,
                product_category varchar(80) NOT NULL,
                image_url varchar(1000) NULL,
                product_url varchar(1000) NULL,
                price numeric(12,2) NULL,
                low_stock boolean NOT NULL,
                availability varchar(40) NOT NULL,
                confidence_score integer NOT NULL,
                observed_at timestamptz NOT NULL,
                source varchar(200) NOT NULL,
                evidence varchar(500) NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_inventory_observations_recent
                ON inventory_observations(observed_at DESC);
            CREATE INDEX IF NOT EXISTS ix_inventory_observations_store_product
                ON inventory_observations(retailer, store_id, product_sku, observed_at DESC);

            CREATE TABLE IF NOT EXISTS inventory_provider_runs (
                id bigserial PRIMARY KEY,
                provider varchar(64) NOT NULL,
                status varchar(32) NOT NULL,
                listings_count integer NOT NULL,
                started_at timestamptz NOT NULL,
                finished_at timestamptz NOT NULL,
                error varchar(1000) NULL
            );
            CREATE INDEX IF NOT EXISTS ix_inventory_provider_runs_recent
                ON inventory_provider_runs(provider, finished_at DESC);
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveObservationsAsync(IReadOnlyList<InventoryListing> listings, CancellationToken cancellationToken)
    {
        if (listings.Count == 0) return;

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in listings)
        {
            var minute = new DateTimeOffset(
                item.ObservedAt.Year, item.ObservedAt.Month, item.ObservedAt.Day,
                item.ObservedAt.Hour, item.ObservedAt.Minute, 0, TimeSpan.Zero);
            var key = $"{item.Retailer}:{item.StoreId}:{item.ProductSku}:{minute:yyyyMMddHHmm}";

            await using var command = new NpgsqlCommand(
                """
                INSERT INTO inventory_observations
                    (observation_key, provider, retailer, store_id, store_name, address, city, region, postal_code,
                     latitude, longitude, product_sku, product_name, product_category, image_url, product_url,
                     price, low_stock, availability, confidence_score, observed_at, source, evidence)
                VALUES
                    (@key, @provider, @retailer, @store, @storeName, @address, @city, @region, @postal,
                     @lat, @lng, @sku, @product, @category, @image, @url,
                     @price, @low, @availability, @confidence, @observed, @source, @evidence)
                ON CONFLICT (observation_key) DO NOTHING
                """, connection, transaction);

            command.Parameters.AddWithValue("key", key);
            command.Parameters.AddWithValue("provider", item.Retailer);
            command.Parameters.AddWithValue("retailer", item.Retailer);
            command.Parameters.AddWithValue("store", item.StoreId);
            command.Parameters.AddWithValue("storeName", item.StoreName);
            command.Parameters.AddWithValue("address", item.Address);
            command.Parameters.AddWithValue("city", item.City);
            command.Parameters.AddWithValue("region", item.Region);
            command.Parameters.AddWithValue("postal", item.PostalCode);
            command.Parameters.AddWithValue("lat", item.Latitude);
            command.Parameters.AddWithValue("lng", item.Longitude);
            command.Parameters.AddWithValue("sku", item.ProductSku);
            command.Parameters.AddWithValue("product", item.ProductName);
            command.Parameters.AddWithValue("category", item.Category);
            command.Parameters.Add(new NpgsqlParameter("image", NpgsqlDbType.Text) { Value = (object?)item.ImageUrl ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("url", NpgsqlDbType.Text) { Value = (object?)item.ProductUrl ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter("price", NpgsqlDbType.Numeric) { Value = (object?)item.Price ?? DBNull.Value });
            command.Parameters.AddWithValue("low", item.LowStock);
            command.Parameters.AddWithValue("availability", item.Availability);
            command.Parameters.AddWithValue("confidence", item.ConfidenceScore);
            command.Parameters.AddWithValue("observed", item.ObservedAt);
            command.Parameters.AddWithValue("source", item.Source);
            command.Parameters.AddWithValue("evidence", item.Evidence);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var cleanup = new NpgsqlCommand(
            "DELETE FROM inventory_observations WHERE observed_at < now() - interval '72 hours'",
            connection, transaction))
        {
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordProviderRunAsync(
        string provider,
        string status,
        int listings,
        DateTimeOffset started,
        DateTimeOffset finished,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO inventory_provider_runs(provider, status, listings_count, started_at, finished_at, error)
            VALUES (@provider, @status, @count, @started, @finished, @error)
            """, connection);
        command.Parameters.AddWithValue("provider", provider);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("count", listings);
        command.Parameters.AddWithValue("started", started);
        command.Parameters.AddWithValue("finished", finished);
        command.Parameters.Add(new NpgsqlParameter("error", NpgsqlDbType.Text) { Value = (object?)error ?? DBNull.Value });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        catch
        {
            return false;
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
