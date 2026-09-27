using System.Globalization;
using PokemonTCG.API.Data;
using PokemonTCG.API.Product;

namespace PokemonTCG.API.Market.Providers;

/// <summary>
/// Verified sold listings from Scrydex's licensed API, raw and graded, each with its own reference, date and link. On
/// only when Scrydex credentials are configured.
/// </summary>
public class ScrydexCompProvider(ScrydexClient client) : ICompProvider
{
    public MarketProvider Id => MarketProvider.Scrydex;
    public string DisplayName => "Scrydex sold listings";
    public bool IsEnabled => client.IsConfigured;
    public string? DisabledReason => IsEnabled ? null : "Needs Scrydex API credentials.";

    public async Task<IReadOnlyList<CompRecord>> FetchSalesAsync(IReadOnlyCollection<CompRequest> printings, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var days = Math.Clamp((int)Math.Ceiling((DateTimeOffset.UtcNow - since).TotalDays), 1, 180);
        var comps = new List<CompRecord>();
        foreach (var printing in printings)
        {
            var data = await client.GetMarketDataAsync(printing.CardId, printing.Variant, days, cancellationToken)
                ?? throw new HttpRequestException("Scrydex didn't return market data.");
            comps.AddRange(data.SoldComps.Select(c => ToRecord(printing, c)));
        }
        return comps;
    }

    public static CompRecord ToRecord(CompRequest printing, ScrydexSoldComp comp) => new(
        printing.CardId,
        printing.Variant,
        comp.Price,
        new DateTimeOffset(comp.SoldAt.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        Currency: comp.Currency,
        // A graded sale keeps its company and grade; a raw one has neither. They're never merged.
        Grade: decimal.TryParse(comp.Grade, NumberStyles.Number, CultureInfo.InvariantCulture, out var grade) ? grade : null,
        GradingCompany: string.IsNullOrWhiteSpace(comp.Company) ? null : comp.Company,
        ProviderReference: $"{comp.Source}:{comp.Id}",
        SourceUrl: comp.Url);
}
