using TcgSignal.Inventory.Domain;
using TcgSignal.Inventory.Infrastructure;

namespace TcgSignal.Inventory.Application;

public sealed class InventorySearchService(
    IEnumerable<IInventoryProvider> providers,
    InventoryStore store,
    TimeProvider clock,
    ILogger<InventorySearchService> logger)
{
    private static readonly ProviderCoverage[] PlannedCoverage =
    [
        new("Target", "Planned", "Adapter reserved; no inventory is shown until a retailer-supported source is validated."),
        new("Walmart", "Planned", "Marketplace APIs expose seller inventory, not trustworthy consumer store shelves; no claims are made from that feed."),
        new("GameStop", "Planned", "Adapter reserved; no inventory is shown until its source and false-positive rate are validated."),
    ];

    public async Task<NearbyInventoryResponse> SearchAsync(InventoryQuery query, CancellationToken cancellationToken)
    {
        var listings = new List<InventoryListing>();
        var coverage = new List<ProviderCoverage>();

        foreach (var provider in providers)
        {
            if (!provider.IsConfigured)
            {
                coverage.Add(new ProviderCoverage(
                    provider.Retailer,
                    "NeedsConfiguration",
                    "Provider integration is implemented but its production credential is not configured."));
                continue;
            }

            var started = clock.GetUtcNow();
            try
            {
                var result = await provider.SearchAsync(query, cancellationToken);
                listings.AddRange(result.Listings);
                coverage.Add(result.Coverage);
                await store.SaveObservationsAsync(result.Listings, cancellationToken);
                await store.RecordProviderRunAsync(
                    provider.Retailer, "Succeeded", result.Listings.Count, started, clock.GetUtcNow(), null, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Inventory provider {Provider} failed", provider.Retailer);
                coverage.Add(new ProviderCoverage(
                    provider.Retailer,
                    "Degraded",
                    "The retailer feed could not be verified right now, so its inventory is hidden.",
                    clock.GetUtcNow()));
                await store.RecordProviderRunAsync(
                    provider.Retailer, "Failed", 0, started, clock.GetUtcNow(),
                    ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message, cancellationToken);
            }
        }

        coverage.AddRange(PlannedCoverage);
        var ordered = listings
            .Where(x => x.DistanceMiles <= query.RadiusMiles)
            .OrderBy(x => x.DistanceMiles)
            .ThenByDescending(x => x.ConfidenceScore)
            .ThenBy(x => x.ProductName)
            .ToList();

        return new NearbyInventoryResponse(
            clock.GetUtcNow(),
            query.RadiusMiles,
            ordered,
            coverage,
            "TCG Signal hides unverifiable retailer results. Confirmed means the retailer's store-availability source reported the item in stock during the displayed check; quantity is never invented.");
    }
}
