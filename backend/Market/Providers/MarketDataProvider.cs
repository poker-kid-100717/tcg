using PokemonTCG.API.Data;

namespace PokemonTCG.API.Market.Providers;

/// <summary>A primary provider fills the catalog and the day's reference prices; an overlay refines them.</summary>
public enum ProviderRole
{
    Primary,
    Overlay,
}

public record ProviderIngestResult(int CardsSeen, int PricesWritten);

/// <summary>
/// A source of daily market reference prices (not individual sales). Each provider writes its own rows, tagged with its
/// <see cref="MarketProvider"/>, inside the snapshot run's transaction. A provider that isn't configured reports
/// <see cref="IsEnabled"/> false and is skipped; one that fails never has its values replaced by another source's under
/// its name.
/// </summary>
public interface IMarketDataProvider
{
    MarketProvider Id { get; }
    string DisplayName { get; }
    ProviderRole Role { get; }
    bool IsEnabled { get; }
    Task<ProviderIngestResult> IngestAsync(DateOnly today, CancellationToken cancellationToken);
}

/// <summary>How one provider did in one snapshot run.</summary>
public record ProviderRunStatus(MarketProvider Provider, string Status, int PricesWritten, string? Error)
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Disabled = "Disabled";
}
