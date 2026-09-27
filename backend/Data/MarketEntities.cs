using Microsoft.EntityFrameworkCore;

namespace PokemonTCG.API.Data;

/// <summary>Where a price or comp came from. Stored as text, so adding a provider needs no data migration.</summary>
public enum MarketProvider
{
    /// <summary>The Pokémon TCG API, which republishes TCGplayer's market/low/mid/high prices daily.</summary>
    PokemonTcg,
    /// <summary>The TCGplayer Developer API (market reference prices by product and printing).</summary>
    TCGPlayer,
    /// <summary>Scrydex: licensed price history and sold listings (raw and graded).</summary>
    Scrydex,
    /// <summary>eBay sold listings, through an approved API. Disabled until credentials and access exist.</summary>
    Ebay,
    /// <summary>A licensed sales-comp data vendor.</summary>
    LicensedCompProvider,
    /// <summary>Entered by hand (e.g. a verified in-person sale).</summary>
    Manual,
}

/// <summary>
/// What an observation says: a market reference (a provider's computed price, not a sale), a listing (an asking price),
/// or a sale (a completed transaction, i.e. a verified sold comp).
/// </summary>
public enum ObservationKind
{
    MarketReference,
    Listing,
    Sale,
}

/// <summary>
/// One price seen at one place and time, with its provenance. Sold comps from transaction-level providers land here;
/// daily market references stay in price_snapshots. Rows are never merged across language, printing, grade or
/// grading company: they're part of <see cref="DedupeKey"/>.
/// </summary>
public class MarketObservation
{
    public long Id { get; set; }
    public MarketProvider Provider { get; set; }
    public ObservationKind Kind { get; set; }
    /// <summary>The provider's own id for the sale or listing, when it has one.</summary>
    public string? ProviderReference { get; set; }
    public string CardId { get; set; } = "";
    /// <summary>The printing: normal, holofoil, reverseHolofoil, 1stEditionHolofoil, unlimitedHolofoil…</summary>
    public string Variant { get; set; } = "";
    /// <summary>ISO 639-1 language of the card (en, ja…).</summary>
    public string Language { get; set; } = "en";
    /// <summary>Raw-card condition (NearMint, LightlyPlayed…), when known.</summary>
    public string? Condition { get; set; }
    /// <summary>Numeric grade (10, 9.5…) for a graded card; null for raw.</summary>
    public decimal? Grade { get; set; }
    /// <summary>PSA, BGS, CGC, SGC… for a graded card; null for raw.</summary>
    public string? GradingCompany { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal Price { get; set; }
    public decimal? Shipping { get; set; }
    /// <summary>When the sale happened (or the listing/reference was observed).</summary>
    public DateTimeOffset ObservedAt { get; set; }
    /// <summary>A link to the source, only where the provider's terms allow storing it.</summary>
    public string? SourceUrl { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
    /// <summary>Unique: the same sale reported twice (or re-ingested) is stored once.</summary>
    public string DedupeKey { get; set; } = "";
}

/// <summary>A market signal that fired for a printing on a given day (see Pricing/Signals).</summary>
public class MarketSignal
{
    public long Id { get; set; }
    public string CardId { get; set; } = "";
    public string Variant { get; set; } = "";
    public DateOnly AsOf { get; set; }
    public string Kind { get; set; } = "";
    public double Value { get; set; }
    public int LookbackDays { get; set; }
    public string Reason { get; set; } = "";
    public int Confidence { get; set; }
    public MarketProvider Provider { get; set; }
}

public partial class AppDbContext
{
    public DbSet<MarketObservation> MarketObservations => Set<MarketObservation>();
    public DbSet<MarketSignal> MarketSignals => Set<MarketSignal>();

    private static void ConfigureMarket(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketObservation>(o =>
        {
            o.ToTable("market_observations");
            o.Property(x => x.Id).HasColumnName("id");
            o.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(30);
            o.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
            o.Property(x => x.ProviderReference).HasColumnName("provider_reference").HasMaxLength(200);
            o.Property(x => x.CardId).HasColumnName("card_id").HasMaxLength(64);
            o.Property(x => x.Variant).HasColumnName("variant").HasMaxLength(40);
            o.Property(x => x.Language).HasColumnName("language").HasMaxLength(8);
            o.Property(x => x.Condition).HasColumnName("condition").HasMaxLength(20);
            o.Property(x => x.Grade).HasColumnName("grade").HasPrecision(3, 1);
            o.Property(x => x.GradingCompany).HasColumnName("grading_company").HasMaxLength(20);
            o.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            o.Property(x => x.Price).HasColumnName("price").HasPrecision(12, 2);
            o.Property(x => x.Shipping).HasColumnName("shipping").HasPrecision(12, 2);
            o.Property(x => x.ObservedAt).HasColumnName("observed_at");
            o.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(1000);
            o.Property(x => x.IngestedAt).HasColumnName("ingested_at");
            o.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(400);
            o.HasIndex(x => x.DedupeKey).IsUnique();
            // Liquidity reads a printing's recent sales.
            o.HasIndex(x => new { x.CardId, x.Variant, x.Kind, x.ObservedAt });
            o.HasOne<Card>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
            o.ToTable(t => t.HasCheckConstraint("ck_market_observations_price", "price >= 0"));
        });

        modelBuilder.Entity<MarketSignal>(s =>
        {
            s.ToTable("market_signals");
            s.Property(x => x.Id).HasColumnName("id");
            s.Property(x => x.CardId).HasColumnName("card_id").HasMaxLength(64);
            s.Property(x => x.Variant).HasColumnName("variant").HasMaxLength(40);
            s.Property(x => x.AsOf).HasColumnName("as_of");
            s.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(40);
            s.Property(x => x.Value).HasColumnName("value");
            s.Property(x => x.LookbackDays).HasColumnName("lookback_days");
            s.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            s.Property(x => x.Confidence).HasColumnName("confidence");
            s.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(30);
            s.HasIndex(x => new { x.CardId, x.Variant, x.AsOf, x.Kind }).IsUnique();
            s.HasIndex(x => new { x.AsOf, x.Kind });
            s.HasOne<Card>().WithMany().HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

/// <summary>Raw-card condition, TCGplayer's scale.</summary>
public enum CardCondition
{
    NearMint,
    LightlyPlayed,
    ModeratelyPlayed,
    HeavilyPlayed,
    Damaged,
}
