using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market.Providers;
using PokemonTCG.API.Pricing;

namespace PokemonTCG.API.Market;

/// <summary>A data source and how it's doing: whether it's on, what it supplies, and its last result.</summary>
public record ProviderStatus(
    MarketProvider Provider,
    string DisplayName,
    /// <summary>"Reference" (computed market prices) or "SoldComps" (individual completed sales).</summary>
    string Supplies,
    bool Enabled,
    string? LastStatus,
    string? Detail);

public record MarketStatus(
    DateTimeOffset? LastSnapshotAt,
    int? CardsSeen,
    int? PricesWritten,
    int DaysOfHistory,
    string PriceSource,
    bool TcgplayerApi,
    DataFreshness Freshness,
    bool SoldCompsAvailable,
    IReadOnlyList<ProviderStatus> Providers);

public class MarketStatusService(
    AppDbContext db,
    IEnumerable<IMarketDataProvider> referenceProviders,
    IEnumerable<ICompProvider> compProviders,
    FreshnessOptions freshness,
    TimeProvider clock)
{
    public async Task<MarketStatus> GetAsync(CancellationToken cancellationToken)
    {
        var lastRun = await db.SnapshotRuns.AsNoTracking()
            .Where(r => r.Status == SnapshotStatus.Succeeded)
            .OrderByDescending(r => r.FinishedAt)
            .FirstOrDefaultAsync(cancellationToken);
        var latestDate = await db.PriceSnapshots.MaxAsync(s => (DateOnly?)s.Date, cancellationToken);
        var days = await db.PriceSnapshots.Select(s => s.Date).Distinct().CountAsync(cancellationToken);
        var lastResults = lastRun?.ProviderResults is { } json
            ? JsonSerializer.Deserialize<List<ProviderRunStatus>>(json, PriceSnapshotService.ProviderJson) ?? []
            : [];

        var providers = referenceProviders.OrderBy(p => p.Role)
            .Select(p =>
            {
                var last = lastResults.FirstOrDefault(r => r.Provider == p.Id);
                return new ProviderStatus(p.Id, p.DisplayName, "Reference", p.IsEnabled, last?.Status, last?.Error);
            })
            .Concat(compProviders.Select(p => new ProviderStatus(p.Id, p.DisplayName, "SoldComps", p.IsEnabled, null, p.DisabledReason)))
            .ToList();
        var overlayOn = providers.Any(p => p is { Provider: MarketProvider.TCGPlayer, Enabled: true });
        var hasSales = await db.MarketObservations.AnyAsync(o => o.Kind == ObservationKind.Sale, cancellationToken);

        return new MarketStatus(
            lastRun?.FinishedAt, lastRun?.CardsSeen, lastRun?.PricesWritten, days,
            overlayOn ? "TCGplayer API" : "Pokémon TCG API (TCGplayer market prices)", overlayOn,
            freshness.Of(latestDate, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)),
            hasSales && compProviders.Any(p => p.IsEnabled), providers);
    }
}
