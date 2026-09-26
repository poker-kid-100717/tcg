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

        // Attributes the price model learns from (see Pricing/Predictions).
        public string? Supertype { get; set; }
        public string[] Subtypes { get; set; } = [];
        /// <summary>The Pokémon on the card (its first National Pokédex number); null for Trainers and Energy.</summary>
        public int? NationalDex { get; set; }
        public string? Artist { get; set; }
        public string? SetSeries { get; set; }
        public DateOnly? SetReleased { get; set; }
        public int? SetPrintedTotal { get; set; }
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

    public enum PredictionStatus
    {
        Running,
        /// <summary>Trained, validated and beat the no-change baseline, so its predictions are shown.</summary>
        Published,
        /// <summary>
        /// Immediate, unvalidated signal built from the latest market-versus-listing pressure while the
        /// trained model is still collecting enough daily history.
        /// </summary>
        Preview,
        /// <summary>Trained but didn't beat the no-change baseline on held-out data, so nothing is shown.</summary>
        Withheld,
        /// <summary>Not enough price history to train and validate yet.</summary>
        InsufficientHistory,
        /// <summary>The latest price snapshot hadn't finished (or failed), so the previous model's predictions stay.</summary>
        Skipped,
        Failed,
    }

    /// <summary>One training run of the price model: what it learned from and how well it did on held-out data.</summary>
    public class PredictionRun
    {
        public int Id { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public PredictionStatus Status { get; set; }
        public DateOnly? AsOf { get; set; }
        public int HorizonDays { get; set; }
        public int TrainingRows { get; set; }
        public int ValidationRows { get; set; }
        /// <summary>Mean absolute error of the predicted change (log return) on the validation period.</summary>
        public double? Mae { get; set; }
        /// <summary>The same error for predicting "no change", the baseline the model has to beat.</summary>
        public double? BaselineMae { get; set; }
        /// <summary>Share of validation moves over 5% whose direction the model got right.</summary>
        public double? DirectionAccuracy { get; set; }
        /// <summary>10th and 90th percentiles of validation residuals, used for each prediction's range.</summary>
        public double? ResidualLow { get; set; }
        public double? ResidualHigh { get; set; }
        /// <summary>Feature importance as JSON: [{ "feature": ..., "weight": ... }].</summary>
        public string? Importance { get; set; }
        public int Predictions { get; set; }
        /// <summary>
        /// At most one run a week keeps its predictions until the horizon passes, to be scored against what
        /// actually happened; other runs' predictions are dropped once a newer run is published.
        /// </summary>
        public bool Checkpoint { get; set; }
        /// <summary>Filled in once the horizon has passed: how the published predictions actually did.</summary>
        public int? RealizedCount { get; set; }
        public double? RealizedMae { get; set; }
        public double? RealizedBaselineMae { get; set; }
        public double? RealizedDirectionAccuracy { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>A card printing's predicted market price <see cref="PredictionRun.HorizonDays"/> after the run's as-of date.</summary>
    public class PricePrediction
    {
        public int RunId { get; set; }
        public string CardId { get; set; } = "";
        public string Variant { get; set; } = "";
        public decimal Current { get; set; }
        public decimal Predicted { get; set; }
        public decimal Low { get; set; }
        public decimal High { get; set; }
        /// <summary>Predicted change in percent, for ranking.</summary>
        public double ChangePercent { get; set; }
        /// <summary>The features that moved this prediction most, as JSON.</summary>
        public string Reasons { get; set; } = "[]";
    }

    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Card> Cards => Set<Card>();
        public DbSet<PriceSnapshot> PriceSnapshots => Set<PriceSnapshot>();
        public DbSet<SnapshotRun> SnapshotRuns => Set<SnapshotRun>();
        public DbSet<PredictionRun> PredictionRuns => Set<PredictionRun>();
        public DbSet<PricePrediction> PricePredictions => Set<PricePrediction>();

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
                card.Property(c => c.Supertype).HasColumnName("supertype").HasMaxLength(40);
                card.Property(c => c.Subtypes).HasColumnName("subtypes");
                card.Property(c => c.NationalDex).HasColumnName("national_dex");
                card.Property(c => c.Artist).HasColumnName("artist").HasMaxLength(200);
                card.Property(c => c.SetSeries).HasColumnName("set_series").HasMaxLength(100);
                card.Property(c => c.SetReleased).HasColumnName("set_released");
                card.Property(c => c.SetPrintedTotal).HasColumnName("set_printed_total");
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

            modelBuilder.Entity<PredictionRun>(run =>
            {
                run.ToTable("prediction_runs");
                run.Property(r => r.Id).HasColumnName("id");
                run.Property(r => r.StartedAt).HasColumnName("started_at");
                run.Property(r => r.FinishedAt).HasColumnName("finished_at");
                run.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
                run.Property(r => r.AsOf).HasColumnName("as_of");
                run.Property(r => r.HorizonDays).HasColumnName("horizon_days");
                run.Property(r => r.TrainingRows).HasColumnName("training_rows");
                run.Property(r => r.ValidationRows).HasColumnName("validation_rows");
                run.Property(r => r.Mae).HasColumnName("mae");
                run.Property(r => r.BaselineMae).HasColumnName("baseline_mae");
                run.Property(r => r.DirectionAccuracy).HasColumnName("direction_accuracy");
                run.Property(r => r.ResidualLow).HasColumnName("residual_low");
                run.Property(r => r.ResidualHigh).HasColumnName("residual_high");
                run.Property(r => r.Importance).HasColumnName("importance").HasColumnType("jsonb");
                run.Property(r => r.Predictions).HasColumnName("predictions");
                run.Property(r => r.Checkpoint).HasColumnName("checkpoint");
                run.Property(r => r.RealizedCount).HasColumnName("realized_count");
                run.Property(r => r.RealizedMae).HasColumnName("realized_mae");
                run.Property(r => r.RealizedBaselineMae).HasColumnName("realized_baseline_mae");
                run.Property(r => r.RealizedDirectionAccuracy).HasColumnName("realized_direction_accuracy");
                run.Property(r => r.Message).HasColumnName("message").HasMaxLength(2000);
            });

            modelBuilder.Entity<PricePrediction>(prediction =>
            {
                prediction.ToTable("price_predictions");
                prediction.HasKey(p => new { p.RunId, p.CardId, p.Variant });
                prediction.Property(p => p.RunId).HasColumnName("run_id");
                prediction.Property(p => p.CardId).HasColumnName("card_id").HasMaxLength(64);
                prediction.Property(p => p.Variant).HasColumnName("variant").HasMaxLength(40);
                prediction.Property(p => p.Current).HasColumnName("current").HasPrecision(12, 2);
                prediction.Property(p => p.Predicted).HasColumnName("predicted").HasPrecision(12, 2);
                prediction.Property(p => p.Low).HasColumnName("low").HasPrecision(12, 2);
                prediction.Property(p => p.High).HasColumnName("high").HasPrecision(12, 2);
                prediction.Property(p => p.ChangePercent).HasColumnName("change_percent");
                prediction.Property(p => p.Reasons).HasColumnName("reasons").HasColumnType("jsonb");
                // Card pages look up a card's predictions in the latest run.
                prediction.HasIndex(p => new { p.CardId, p.RunId });
                prediction.HasOne<PredictionRun>().WithMany().HasForeignKey(p => p.RunId).OnDelete(DeleteBehavior.Cascade);
                prediction.HasOne<Card>().WithMany().HasForeignKey(p => p.CardId).OnDelete(DeleteBehavior.Cascade);
            });
        }
    }

    public static class DbInitializer
    {
        public static void Initialize(AppDbContext context) => context.Database.Migrate();
    }
}
