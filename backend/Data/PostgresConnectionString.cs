using Npgsql;

namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Npgsql only understands key/value connection strings, but hosted
    /// Postgres providers (Neon, Supabase, Heroku, ...) hand out
    /// postgres:// URLs. This accepts either form. GSS (Kerberos) encryption
    /// is disabled unless explicitly required: no target host uses it, and probing for
    /// it fails noisily on slim container images without Kerberos libraries.
    /// </summary>
    public static class PostgresConnectionString
    {
        public static string Normalize(string connectionString)
        {
            var builder = IsUrl(connectionString)
                ? FromUrl(connectionString)
                : new NpgsqlConnectionStringBuilder(connectionString);

            // Npgsql's default is Prefer, which probes for GSS on every connect.
            if (builder.GssEncryptionMode == GssEncryptionMode.Prefer)
            {
                builder.GssEncryptionMode = GssEncryptionMode.Disable;
            }

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
                        builder.SslMode = Enum.Parse<SslMode>(value, ignoreCase: true);
                        break;
                    // libpq-only options Npgsql doesn't support (e.g. Neon's
                    // channel_binding) are dropped rather than failing startup.
                    default:
                        break;
                }
            }

            return builder;
        }
    }
}
