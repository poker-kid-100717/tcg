using Microsoft.EntityFrameworkCore;

namespace PokemonTCG.API.Data;

/// <summary>A set in the local catalog, refreshed by each price snapshot so set pages don't wait on the card database.</summary>
public class CardSet
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Series { get; set; } = "";
    public int PrintedTotal { get; set; }
    public int Total { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? LogoUrl { get; set; }
    public string? SymbolUrl { get; set; }
    /// <summary>The matching TCGplayer group, once the TCGplayer catalog has been matched.</summary>
    public int? TcgplayerGroupId { get; set; }
}

/// <summary>
/// The newest known price of every printing, including cheap ones that price_snapshots skips to stay small.
/// One row per card and printing, overwritten by each snapshot.
/// </summary>
public class LatestPrice
{
    public string CardId { get; set; } = "";
    public string Variant { get; set; } = "";
    public decimal? Market { get; set; }
    public decimal? Low { get; set; }
    public decimal? Mid { get; set; }
    public decimal? High { get; set; }
    public DateOnly UpdatedOn { get; set; }
    /// <summary>Which provider the price came from (see <see cref="MarketProvider"/>).</summary>
    public MarketProvider Provider { get; set; } = MarketProvider.PokemonTcg;
}

public partial class AppDbContext
{
    private static void ConfigureCatalog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardSet>(set =>
        {
            set.ToTable("sets");
            set.Property(s => s.Id).HasColumnName("id").HasMaxLength(64);
            set.Property(s => s.Name).HasColumnName("name").HasMaxLength(200);
            set.Property(s => s.Series).HasColumnName("series").HasMaxLength(100);
            set.Property(s => s.PrintedTotal).HasColumnName("printed_total");
            set.Property(s => s.Total).HasColumnName("total");
            set.Property(s => s.ReleaseDate).HasColumnName("release_date");
            set.Property(s => s.LogoUrl).HasColumnName("logo_url").HasMaxLength(500);
            set.Property(s => s.SymbolUrl).HasColumnName("symbol_url").HasMaxLength(500);
            set.Property(s => s.TcgplayerGroupId).HasColumnName("tcgplayer_group_id");
        });

        modelBuilder.Entity<LatestPrice>(price =>
        {
            price.ToTable("latest_prices");
            price.HasKey(p => new { p.CardId, p.Variant });
            price.Property(p => p.CardId).HasColumnName("card_id").HasMaxLength(64);
            price.Property(p => p.Variant).HasColumnName("variant").HasMaxLength(40);
            price.Property(p => p.Market).HasColumnName("market").HasPrecision(12, 2);
            price.Property(p => p.Low).HasColumnName("low").HasPrecision(12, 2);
            price.Property(p => p.Mid).HasColumnName("mid").HasPrecision(12, 2);
            price.Property(p => p.High).HasColumnName("high").HasPrecision(12, 2);
            price.Property(p => p.UpdatedOn).HasColumnName("updated_on");
            price.Property(p => p.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(30).HasDefaultValue(MarketProvider.PokemonTcg);
            price.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
