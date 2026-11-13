using System.Net.Http.Json;
using System.Text.Json;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// TCGCSV (https://tcgcsv.com) is a free, community-run daily mirror of TCGplayer's catalog and prices,
/// including sealed product. Its guidelines: sync at most once a day, read from our own copy rather than
/// from user requests, send an identifying User-Agent, and pause between requests.
/// TCGCSV tolerates free tools but warns against profit-seeking ones, so TCG Signal uses it only while
/// billing is off; a licensed provider must replace it before paid plans return.
/// </summary>
public class TcgcsvOptions
{
    public const string SectionName = "Tcgcsv";

    public string BaseUrl { get; set; } = "https://tcgcsv.com/";

    /// <summary>TCGplayer's category for English Pokémon.</summary>
    public int CategoryId { get; set; } = 3;

    /// <summary>Pause between requests (TCGCSV asks for roughly 100 ms).</summary>
    public int RequestDelayMilliseconds { get; set; } = 150;
}

public record TcgcsvGroup(int GroupId, string Name, string? Abbreviation, bool? IsSupplemental, string? PublishedOn)
{
    public DateOnly? Published =>
        DateTime.TryParse(PublishedOn, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var value)
            ? DateOnly.FromDateTime(value)
            : null;
}

public record TcgcsvExtendedData(string Name, string? DisplayName, string? Value);

public record TcgcsvPresale(bool? IsPresale, string? ReleasedOn, string? Note);

public record TcgcsvProduct(
    int ProductId,
    string Name,
    string? CleanName,
    string? ImageUrl,
    int GroupId,
    string? Url,
    TcgcsvPresale? PresaleInfo,
    IReadOnlyList<TcgcsvExtendedData>? ExtendedData)
{
    /// <summary>The printed number for a single card, e.g. "006/165". Sealed product has none.</summary>
    public string? Number => Extended("Number");

    public string? Rarity => Extended("Rarity");

    private string? Extended(string name) =>
        ExtendedData?.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value is { Length: > 0 } value
            ? value
            : null;
}

/// <summary>One price row. A product has one per printing (<see cref="SubTypeName"/>, e.g. "Reverse Holofoil").</summary>
public record TcgcsvPrice(
    int ProductId,
    decimal? LowPrice,
    decimal? MidPrice,
    decimal? HighPrice,
    decimal? MarketPrice,
    decimal? DirectLowPrice,
    string? SubTypeName);

public record TcgcsvList<T>(IReadOnlyList<T> Results);

public class TcgcsvClient(HttpClient http, TcgcsvOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<IReadOnlyList<TcgcsvGroup>> GetGroupsAsync(CancellationToken cancellationToken) =>
        GetListAsync<TcgcsvGroup>($"tcgplayer/{options.CategoryId}/groups", cancellationToken);

    public Task<IReadOnlyList<TcgcsvProduct>> GetProductsAsync(int groupId, CancellationToken cancellationToken) =>
        GetListAsync<TcgcsvProduct>($"tcgplayer/{options.CategoryId}/{groupId}/products", cancellationToken);

    public Task<IReadOnlyList<TcgcsvPrice>> GetPricesAsync(int groupId, CancellationToken cancellationToken) =>
        GetListAsync<TcgcsvPrice>($"tcgplayer/{options.CategoryId}/{groupId}/prices", cancellationToken);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TcgcsvList<T>>(Json, cancellationToken);
        return body?.Results ?? [];
    }
}
