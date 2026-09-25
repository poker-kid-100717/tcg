using Microsoft.EntityFrameworkCore;

namespace PokemonTCG.API.Data
{
    /// <summary>A card the price snapshots have seen, kept so price queries don't need the upstream API.</summary>
    public class Card
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public string SetId { get; set; } = "";
        public string SetName { get; set; } = "";
        public string? Rarity { get; set; }
        public string? ImageSmall { get; set; }
        public string? TcgplayerUrl { get; set; }
    }

    /// <summary>
    /// One TCGplayer price for one printing (variant) of a card on one day.
    /// A day's run is idempotent: rerunning it overwrites that day's rows.
    /// </summary>
    public class PriceSnapshot
    {
        public string CardId { get; set; } = "";
        public string Variant { get; set; } = "";
        public DateOnly Date { get; set; }
        public decimal? Market { get; set; }
        public decimal? Low { get; set; }
        public decimal? Mid { get; set; }
        public decimal? High { get; set; }
    }

    public enum SnapshotStatus
    {
        Running,
        Succeeded,
        Failed,
    }

    /// <summary>A record of each price collection run, for observability.</summary>
    public class SnapshotRun
    {
        public int Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public SnapshotStatus Status { get; set; }
        public int CardsSeen { get; set; }
        public int PricesWritten { get; set; }
        public string? Error { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Card> Cards => Set<Card>();
        public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
        public DbSet<SnapshotRun> SnapshotRuns => Set<SnapshotRun>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Card>(card =>
            {
                card.ToTable("cards");
                card.Property(c => c.Id).HasColumnName("id").HasMaxLength(64);
                card.Property(c => c.Name).HasColumnName("name").HasMaxLength(200);
                card.Property(c => c.Number).HasColumnName("number").HasMaxLength(32);
                card.Property(c => c.SetId).HasColumnName("set_id").HasMaxLength(64);
                card.Property(c => c.SetName).HasColumnName("set_name").HasMaxLength(200);
                card.Property(c => c.Rarity).HasColumnName("rarity").HasMaxLength(100);
                card.Property(c => c.ImageSmall).HasColumnName("image_small").HasMaxLength(500);
                card.Property(c => c.TcgplayerUrl).HasColumnName("tcgplayer_url").HasMaxLength(500);
                card.HasIndex(c => c.SetId);
            });

            modelBuilder.Entity<PriceSnapshot>(snapshot =>
            {
                snapshot.ToTable("price_snapshots");
                snapshot.HasKey(s => new { s.CardId, s.Variant, s.Date });
                snapshot.Property(s => s.CardId).HasColumnName("card_id").HasMaxLength(64);
                snapshot.Property(s => s.Variant).HasColumnName("variant").HasMaxLength(40);
                snapshot.Property(s => s.Date).HasColumnName("date");
                snapshot.Property(s => s.Market).HasColumnName("market").HasPrecision(12, 2);
                snapshot.Property(s => s.Low).HasColumnName("low").HasPrecision(12, 2);
                snapshot.Property(s => s.Mid).HasColumnName("mid").HasPrecision(12, 2);
                snapshot.Property(s => s.High).HasColumnName("high").HasPrecision(12, 2);
                // Movers and retention both filter by day across all cards.
                snapshot.HasIndex(s => s.Date);
                snapshot.HasOne<Card>().WithMany().HasForeignKey(s => s.CardId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<SnapshotRun>(run =>
            {
                run.ToTable("snapshot_runs");
                run.Property(r => r.Id).HasColumnName("id");
                run.Property(r => r.StartedAt).HasColumnName("started_at");
                run.Property(r => r.FinishedAt).HasColumnName("finished_at");
                run.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20);
                run.Property(r => r.CardsSeen).HasColumnName("cards_seen");
                run.Property(r => r.PricesWritten).HasColumnName("prices_written");
                run.Property(r => r.Error).HasColumnName("error").HasMaxLength(2000);
            });
        }
    }

    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context) => context.Database.Migrate();
    }
}
