using System.Globalization;
using System.Text.Json.Serialization;

namespace PokemonTCG.API.Pricing;

// Shapes returned by the Pokémon TCG API v2 (https://docs.pokemontcg.io).
// Only the fields the price guide uses are mapped.

public record PagedResponse<T>(
    [property: JsonPropertyName("data")] IReadOnlyList<T> Data,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalCount")] int TotalCount);

public record SingleResponse<T>([property: JsonPropertyName("data")] T Data);

public record TcgSetImages(string? Symbol, string? Logo);

public record TcgSet(
    string Id,
    string Name,
    string Series,
    int PrintedTotal,
    int Total,
    string ReleaseDate,
    TcgSetImages? Images)
{
    /// <summary>The API formats dates as yyyy/MM/dd.</summary>
    public DateOnly? Released => ParseDate(ReleaseDate);

    internal static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy/MM/dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

public record TcgCardImages(string? Small, string? Large);

public record TcgPrice(decimal? Low, decimal? Mid, decimal? High, decimal? Market, decimal? DirectLow);

public record TcgPlayer(string? Url, string? UpdatedAt, IReadOnlyDictionary<string, TcgPrice>? Prices)
{
    public DateOnly? Updated => TcgSet.ParseDate(UpdatedAt);
}

public record TcgCard(
    string Id,
    string Name,
    string? Supertype,
    IReadOnlyList<string>? Subtypes,
    string? Hp,
    IReadOnlyList<string>? Types,
    TcgSet Set,
    string Number,
    string? Artist,
    string? Rarity,
    string? FlavorText,
    TcgCardImages? Images,
    TcgPlayer? Tcgplayer,
    IReadOnlyList<int>? NationalPokedexNumbers = null);
