using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PokemonTCG.API.Data
{
    /// <summary>
    /// Lets the EF Core CLI (`dotnet ef migrations add`, `has-pending-model-changes`)
    /// build the model without starting the web host, so it never needs a JWT key
    /// or a reachable database. The connection string is only used if a command
    /// actually connects (e.g. `dotnet ef database update`).
    /// </summary>
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var connectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=pokemontcg;Username=postgres;Password=postgres";

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new AppDbContext(options);
        }
    }
}
