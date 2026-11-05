using System.Text.Json.Serialization;

namespace PokemonTCG.API.Pricing.Tcgplayer;

// Shapes returned by TCGplayer's Developer API. Only the fields this app uses
// are mapped; property names follow the API's camelCase JSON.

/// <summary>The envelope every catalog and pricing endpoint wraps its results in.</summary>
public record TcgplayerResponse<T>(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("errors")] IReadOnlyList<string>? Errors,
    [property: JsonPropertyName("totalItems")] int? TotalItems,
    [property: JsonPropertyName("results")] IReadOnlyList<T>? Results);

/// <summary>Response of POST /token (OAuth client credentials).</summary>
public record TcgplayerToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] long ExpiresIn);

/// <summary>A TCGplayer "group": for Pokémon, one per set (or subset/promo line).</summary>
public record TcgplayerGroup(
    int GroupId,
    string Name,
    string? Abbreviation,
    bool IsSupplemental,
    DateTime? PublishedOn,
    DateTime? ModifiedOn,
    int CategoryId);

public record TcgplayerExtendedData(string Name, string? DisplayName, string? Value);

public record TcgplayerProduct(
    int ProductId,
    string Name,
    string? CleanName,
    string? ImageUrl,
    int CategoryId,
    int GroupId,
    string? Url,
    DateTime? ModifiedOn,
    IReadOnlyList<TcgplayerExtendedData>? ExtendedData)
{
    /// <summary>The value of the extended field <paramref name="name"/> ("Number", "Rarity", "CardType", "HP", "Stage"...).</summary>
    public string? GetExtendedValue(string name) =>
        ExtendedData?.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    /// <summary>The printed collector number, e.g. "006/165", "TG05/TG30", "SWSH101".</summary>
    public string? Number => GetExtendedValue("Number");
}

/// <summary>Market prices for one product in one printing (<see cref="SubTypeName"/>, e.g. "Reverse Holofoil").</summary>
public record TcgplayerProductPrice(
    int ProductId,
    decimal? LowPrice,
    decimal? MidPrice,
    decimal? HighPrice,
    decimal? MarketPrice,
    decimal? DirectLowPrice,
    string SubTypeName);

/// <summary>A SKU: one product in one language, printing and condition.</summary>
public record TcgplayerSku(int SkuId, int ProductId, int LanguageId, int PrintingId, int ConditionId);

public record TcgplayerSkuPrice(
    int SkuId,
    decimal? LowPrice,
    decimal? LowestShipping,
    decimal? LowestListingPrice,
    decimal? MarketPrice,
    decimal? DirectLowPrice);

public record TcgplayerCondition(int ConditionId, string Name, string? Abbreviation, int DisplayOrder);

public record TcgplayerPrinting(int PrintingId, string Name, int DisplayOrder, DateTime? ModifiedOn);

public record TcgplayerLanguage(int LanguageId, string Name, string? Abbr);

/// <summary>TCGplayer answered with success=false (or a non-success status) for a reason other than "nothing found".</summary>
public class TcgplayerApiException(string message, IReadOnlyList<string> errors, int? statusCode = null)
    : Exception(message)
{
    public IReadOnlyList<string> Errors { get; } = errors;

    /// <summary>The HTTP status, when the failure came with one other than 200.</summary>
    public int? StatusCode { get; } = statusCode;
}
