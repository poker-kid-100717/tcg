using Npgsql;
using PokemonTCG.API.Data;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class PostgresConnectionStringTests
    {
        [Fact]
        public void Normalize_KeepsKeyValueSettings()
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                "Host=localhost;Port=5432;Database=pokemontcg;Username=postgres;Password=postgres"));

            Assert.Equal("localhost", result.Host);
            Assert.Equal("pokemontcg", result.Database);
            Assert.Equal("postgres", result.Username);
            Assert.Equal("postgres", result.Password);
        }

        [Fact]
        public void Normalize_DisablesGssEncryptionByDefault()
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize("Host=localhost"));

            Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);
        }

        [Fact]
        public void Normalize_RespectsExplicitGssEncryptionMode()
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                "Host=localhost;GSS Encryption Mode=Require"));

            Assert.Equal(GssEncryptionMode.Require, result.GssEncryptionMode);
        }

        [Fact]
        public void Normalize_ConvertsNeonStyleUrl()
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                "postgresql://app_user:p%40ss%3Aword@ep-cool-name-123.us-east-2.aws.neon.tech/pokemontcg?sslmode=require&channel_binding=require"));

            Assert.Equal("ep-cool-name-123.us-east-2.aws.neon.tech", result.Host);
            Assert.Equal(5432, result.Port);
            Assert.Equal("pokemontcg", result.Database);
            Assert.Equal("app_user", result.Username);
            Assert.Equal("p@ss:word", result.Password);
            Assert.Equal(SslMode.Require, result.SslMode);
            Assert.Equal(ChannelBinding.Require, result.ChannelBinding);
        }

        [Theory]
        [InlineData("verify-full", SslMode.VerifyFull)]
        [InlineData("verify-ca", SslMode.VerifyCA)]
        [InlineData("disable", SslMode.Disable)]
        public void Normalize_AcceptsLibpqSslModeSpelling(string sslMode, SslMode expected)
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                $"postgres://u:p@db.example.com/app?sslmode={sslMode}"));

            Assert.Equal(expected, result.SslMode);
        }

        [Fact]
        public void Normalize_KeepsExplicitPort()
        {
            var result = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                "postgres://u:p@db.example.com:6543/app"));

            Assert.Equal(6543, result.Port);
        }
    }
}
