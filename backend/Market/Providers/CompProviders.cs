using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PokemonTCG.API.Data;

namespace PokemonTCG.API.Market.Providers;

/// <summary>A printing whose sales to fetch.</summary>
public record CompRequest(string CardId, string Variant, string? TcgplayerUrl);

/// <summary>One completed sale as a provider reports it, with its provenance.</summary>
public record CompRecord(
    string CardId,
    string Variant,
    decimal Price,
    DateTimeOffset SoldAt,
    string Currency = "USD",
    string Language = "en",
    string? Condition = null,
    decimal? Grade = null,
    string? GradingCompany = null,
    decimal? Shipping = null,
    string? ProviderReference = null,
    string? SourceUrl = null);

/// <summary>
/// A source of transaction-level sold comps (verified sales, not reference prices). Providers must use an approved
/// API: no HTML or search-result scraping. A provider without access reports <see cref="IsEnabled"/> false with the
/// reason, and liquidity stays Unknown rather than being estimated.
/// </summary>
public interface ICompProvider
{
    MarketProvider Id { get; }
    string DisplayName { get; }
    bool IsEnabled { get; }
    /// <summary>Why the provider is off, shown on the status page (null when enabled).</summary>
    string? DisabledReason { get; }
    Task<IReadOnlyList<CompRecord>> FetchSalesAsync(IReadOnlyCollection<CompRequest> printings, DateTimeOffset since, CancellationToken cancellationToken);
}

public class EbayOptions
{
    public const string SectionName = "Ebay";
    /// <summary>Only set once eBay has approved access to sold-item data (Marketplace Insights) for this app.</summary>
    public bool SoldItemsAccessApproved { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public bool IsConfigured => SoldItemsAccessApproved && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

/// <summary>
/// The integration boundary for eBay sold comps. eBay's sold-listing data is only available through restricted APIs that
/// need eBay's approval, so this provider stays disabled until that access and credentials exist. It deliberately has no
/// fallback: it never scrapes eBay pages or search results.
/// </summary>
public class EbayCompProvider(IOptions<EbayOptions> options) : ICompProvider
{
    public MarketProvider Id => MarketProvider.Ebay;
    public string DisplayName => "eBay sold listings";
    public bool IsEnabled => false;

    public string? DisabledReason => options.Value.IsConfigured
        ? "eBay access is configured, but the sold-comps client hasn't been built against the approved API yet."
        : "Needs eBay's approval for sold-item data and API credentials.";

    public Task<IReadOnlyList<CompRecord>> FetchSalesAsync(IReadOnlyCollection<CompRequest> printings, DateTimeOffset since, CancellationToken cancellationToken) =>
        throw new NotSupportedException(DisabledReason);
}

/// <summary>Builds the key that makes a stored comp unique.</summary>
public static class ObservationKeys
{
    /// <summary>
    /// With a provider reference, the provider's own id identifies the sale. Without one, every attribute that makes
    /// two sales different (printing, language, condition, grade, grading company, price, time) is part of the key, so
    /// different printings or grades are never collapsed into one.
    /// </summary>
    public static string Dedupe(MarketProvider provider, ObservationKind kind, CompRecord comp)
    {
        if (!string.IsNullOrWhiteSpace(comp.ProviderReference)) return $"{provider}|{kind}|ref|{comp.ProviderReference.Trim()}";
        return string.Join('|',
            provider, kind, comp.CardId, comp.Variant, comp.Language.ToLowerInvariant(), comp.Condition ?? "-",
            comp.GradingCompany?.ToUpperInvariant() ?? "raw", comp.Grade?.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            comp.Currency.ToUpperInvariant(), comp.Price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            comp.SoldAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}

public record CompIngestResult(MarketProvider Provider, string Status, int Fetched, int Stored, string? Detail);

/// <summary>Pulls recent sales from every enabled comp provider into market_observations, idempotently.</summary>
public class CompIngestionService(IEnumerable<ICompProvider> providers, AppDbContext db, TimeProvider clock, ILogger<CompIngestionService> logger)
{
    public async Task<IReadOnlyList<CompIngestResult>> IngestAsync(IReadOnlyCollection<CompRequest> printings, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var results = new List<CompIngestResult>();
        foreach (var provider in providers)
        {
            if (!provider.IsEnabled)
            {
                results.Add(new(provider.Id, ProviderRunStatus.Disabled, 0, 0, provider.DisabledReason));
                continue;
            }
            try
            {
                var comps = await provider.FetchSalesAsync(printings, since, cancellationToken);
                results.Add(new(provider.Id, ProviderRunStatus.Succeeded, comps.Count, await StoreAsync(provider.Id, comps, cancellationToken), null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Comp provider {Provider} failed", provider.Id);
                results.Add(new(provider.Id, ProviderRunStatus.Failed, 0, 0, "The provider didn't respond as expected."));
            }
        }
        return results;
    }

    /// <summary>Stores new sales; ones already stored (same dedupe key) are skipped. Returns how many were new.</summary>
    public async Task<int> StoreAsync(MarketProvider provider, IReadOnlyCollection<CompRecord> comps, CancellationToken cancellationToken)
    {
        var valid = comps.Where(c => c.Price >= 0 && c.Price < 10_000_000 && !string.IsNullOrWhiteSpace(c.CardId) && !string.IsNullOrWhiteSpace(c.Variant)).ToList();
        if (valid.Count == 0) return 0;
        var now = clock.GetUtcNow();
        var rows = valid
            .Select(c => new MarketObservation
            {
                Provider = provider, Kind = ObservationKind.Sale, ProviderReference = c.ProviderReference, CardId = c.CardId, Variant = c.Variant,
                Language = c.Language.ToLowerInvariant(), Condition = c.Condition, Grade = c.Grade, GradingCompany = c.GradingCompany?.ToUpperInvariant(),
                Currency = c.Currency.ToUpperInvariant(), Price = c.Price, Shipping = c.Shipping, ObservedAt = c.SoldAt.ToUniversalTime(),
                SourceUrl = c.SourceUrl, IngestedAt = now, DedupeKey = ObservationKeys.Dedupe(provider, ObservationKind.Sale, c),
            })
            .DistinctBy(r => r.DedupeKey)
            .ToList();
        var keys = rows.Select(r => r.DedupeKey).ToList();
        var known = (await db.MarketObservations.Where(o => keys.Contains(o.DedupeKey)).Select(o => o.DedupeKey).ToListAsync(cancellationToken)).ToHashSet();
        var fresh = rows.Where(r => !known.Contains(r.DedupeKey)).ToList();
        if (fresh.Count == 0) return 0;
        db.MarketObservations.AddRange(fresh);
        await db.SaveChangesAsync(cancellationToken);
        return fresh.Count;
    }
}
