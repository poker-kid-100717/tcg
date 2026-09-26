using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing.Tcgplayer;

namespace PokemonTCG.API.Market.Providers;

/// <summary>
/// The TCGplayer Developer API as an overlay: with keys configured, the day's reference prices for every matched
/// product come straight from TCGplayer and are tagged <see cref="MarketProvider.TCGPlayer"/>. Without keys it's
/// disabled and the Pokémon TCG API's copies stand, tagged as theirs.
/// </summary>
public class TcgplayerMarketDataProvider(TcgplayerPriceSync sync) : IMarketDataProvider
{
    public MarketProvider Id => MarketProvider.TCGPlayer;
    public string DisplayName => "TCGplayer API";
    public ProviderRole Role => ProviderRole.Overlay;
    public bool IsEnabled => sync.IsConfigured;

    public async Task<ProviderIngestResult> IngestAsync(DateOnly today, CancellationToken cancellationToken) =>
        new(0, await sync.SyncAsync(today, cancellationToken));
}
