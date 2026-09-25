using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace PokemonTCG.API.Pricing;

public class PokemonTcgOptions
{
    public const string SectionName = "PokemonTcgApi";

    public string BaseUrl { get; set; } = "https://api.pokemontcg.io/v2/";

    /// <summary>Optional. Raises the upstream rate limit; stays on the server.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Typed client for the Pokémon TCG API. Every call site goes through here,
/// so query building and escaping live in one place.
/// </summary>
public class PokemonTcgClient(HttpClient http)
{
    public const int MaxPageSize = 250;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TcgSet>> GetSetsAsync(CancellationToken cancellationToken)
    {
        var response = await GetAsync<PagedResponse<TcgSet>>("sets?orderBy=-releaseDate&pageSize=250", cancellationToken);
        return response?.Data ?? [];
    }

    public async Task<TcgSet?> GetSetAsync(string setId, CancellationToken cancellationToken) =>
        (await GetAsync<SingleResponse<TcgSet>>($"sets/{Uri.EscapeDataString(setId)}", cancellationToken))?.Data;

    public async Task<TcgCard?> GetCardAsync(string cardId, CancellationToken cancellationToken) =>
        (await GetAsync<SingleResponse<TcgCard>>($"cards/{Uri.EscapeDataString(cardId)}", cancellationToken))?.Data;

    /// <summary>Every card in a set, following pagination (large sets exceed one page).</summary>
    public async Task<IReadOnlyList<TcgCard>> GetSetCardsAsync(string setId, CancellationToken cancellationToken)
    {
        var cards = new List<TcgCard>();
        for (var page = 1; ; page++)
        {
            var response = await GetCardPageAsync($"set.id:{Quote(setId)}", page, MaxPageSize, cancellationToken);
            cards.AddRange(response.Data);
            if (response.Data.Count == 0 || cards.Count >= response.TotalCount) break;
        }
        return cards;
    }

    /// <summary>Cards whose name starts with <paramref name="name"/> (case-insensitive), newest sets first.</summary>
    public Task<PagedResponse<TcgCard>> SearchCardsAsync(string name, int page, int pageSize, CancellationToken cancellationToken) =>
        GetCardPageAsync($"name:{Quote($"{name}*")}", page, pageSize, cancellationToken, orderBy: "-set.releaseDate,number");

    public Task<PagedResponse<TcgCard>> GetAllCardsPageAsync(int page, CancellationToken cancellationToken) =>
        GetCardPageAsync(query: null, page, MaxPageSize, cancellationToken);

    private async Task<PagedResponse<TcgCard>> GetCardPageAsync(
        string? query, int page, int pageSize, CancellationToken cancellationToken, string? orderBy = null)
    {
        var path = new StringBuilder($"cards?page={page}&pageSize={Math.Clamp(pageSize, 1, MaxPageSize)}");
        if (query is not null) path.Append("&q=").Append(Uri.EscapeDataString(query));
        if (orderBy is not null) path.Append("&orderBy=").Append(Uri.EscapeDataString(orderBy));
        return await GetAsync<PagedResponse<TcgCard>>(path.ToString(), cancellationToken)
               ?? new PagedResponse<TcgCard>([], page, pageSize, 0);
    }

    /// <summary>
    /// Wraps a value in quotes for the API's Lucene-style query language,
    /// dropping characters that would end the quoted term or add clauses.
    /// </summary>
    public static string Quote(string value)
    {
        var cleaned = new string(value.Where(c => c is not ('"' or '\\') && !char.IsControl(c)).ToArray()).Trim();
        return $"\"{cleaned}\"";
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }
}
