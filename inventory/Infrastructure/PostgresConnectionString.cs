using Npgsql;

namespace TcgSignal.Inventory.Infrastructure;

public static class PostgresConnectionString
{
    public static string Normalize(string connectionString)
    {
        var builder = IsUrl(connectionString)
            ? FromUrl(connectionString)
            : new NpgsqlConnectionStringBuilder(connectionString);

        if (builder.GssEncryptionMode == GssEncryptionMode.Prefer)
            builder.GssEncryptionMode = GssEncryptionMode.Disable;

        return builder.ConnectionString;
    }

    private static bool IsUrl(string connectionString) =>
        connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static NpgsqlConnectionStringBuilder FromUrl(string connectionString)
    {
        var uri = new Uri(connectionString);
        var userInfo = uri.UserInfo.Split(':', 2);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort || uri.Port <= 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
        };

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    builder.SslMode = Enum.Parse<SslMode>(value.Replace("-", ""), true);
                    break;
                case "channel_binding":
                    builder.ChannelBinding = Enum.Parse<ChannelBinding>(value, true);
                    break;
                case "gssencmode":
                    builder.GssEncryptionMode = Enum.Parse<GssEncryptionMode>(value, true);
                    break;
            }
        }

        return builder;
    }
}
