using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>
/// Typed client for TCGplayer's Developer API (catalog and pricing).
/// The caller sets <see cref="HttpClient.BaseAddress"/> to {BaseUrl}/{ApiVersion}/
/// (see <see cref="TcgplayerOptions.VersionedBaseUri"/>) and puts
/// <see cref="TcgplayerAuthHandler"/> in the pipeline.
/// </summary>
/// <remarks>
/// Verification: docs.tcgplayer.com was not reachable when this was written
/// (blocked by the build environment's egress proxy), so NOTHING here was
/// checked against the live documentation. Paths, query parameters, field names
/// and the response envelope follow the shapes specified for this module, which
/// match TCGplayer's publicly documented v1.x API as commonly used
/// (catalog/categories/{id}/groups, catalog/products, catalog/products/{ids}/skus,
/// pricing/group/{id}, pricing/product/{ids}, pricing/sku/{ids},
/// catalog/categories/{id}/conditions|printings|languages). Re-check against the
/// docs once API keys arrive, particularly: the maximum ids per path segment
/// (assumed 250), whether id-list endpoints page, and the exact "nothing found" error texts.
/// </remarks>
public class TcgplayerClient(HttpClient http, IOptions<TcgplayerOptions> options)
{
    /// <summary>Largest page the paged catalog endpoints accept.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Most ids sent in one comma-separated path segment.</summary>
    public const int MaxIdsPerRequest = 250;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // TCGplayer reports "nothing matched" as success=false with one of these.
    private static readonly string[] NotFoundErrors = ["no products were found", "no results", "no skus were found", "no groups were found"];

    private readonly TcgplayerOptions _options = options.Value;

    /// <summary>All groups (sets) in a category.</summary>
    public Task<IReadOnlyList<TcgplayerGroup>> GetGroupsAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetAllPagesAsync<TcgplayerGroup>($"catalog/categories/{categoryId}/groups", cancellationToken);

    /// <summary>All card products in a group, with extended fields (Number, Rarity, CardType, HP, Stage...).</summary>
    public Task<IReadOnlyList<TcgplayerProduct>> GetGroupProductsAsync(int groupId, CancellationToken cancellationToken = default) =>
        GetAllPagesAsync<TcgplayerProduct>(
            $"catalog/products?groupId={groupId}&categoryId={_options.CategoryId}&productTypes=Cards&getExtendedFields=true",
            cancellationToken);

    /// <summary>Market prices for every product in a group, one row per product and printing.</summary>
    public Task<IReadOnlyList<TcgplayerProductPrice>> GetGroupPricesAsync(int groupId, CancellationToken cancellationToken = default) =>
        GetListAsync<TcgplayerProductPrice>($"pricing/group/{groupId}", cancellationToken);

    /// <summary>Market prices for the given products, batched <see cref="MaxIdsPerRequest"/> per call.</summary>
    public Task<IReadOnlyList<TcgplayerProductPrice>> GetProductPricesAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default) =>
        GetBatchedAsync<TcgplayerProductPrice>(productIds, ids => $"pricing/product/{ids}", cancellationToken);

    /// <summary>Every SKU (language × printing × condition) of the given products, batched.</summary>
    public Task<IReadOnlyList<TcgplayerSku>> GetSkusAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default) =>
        GetBatchedAsync<TcgplayerSku>(productIds, ids => $"catalog/products/{ids}/skus", cancellationToken);

    /// <summary>Market prices for the given SKUs, batched.</summary>
    public Task<IReadOnlyList<TcgplayerSkuPrice>> GetSkuPricesAsync(IEnumerable<int> skuIds, CancellationToken cancellationToken = default) =>
        GetBatchedAsync<TcgplayerSkuPrice>(skuIds, ids => $"pricing/sku/{ids}", cancellationToken);

    public Task<IReadOnlyList<TcgplayerCondition>> GetConditionsAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetListAsync<TcgplayerCondition>($"catalog/categories/{categoryId}/conditions", cancellationToken);

    public Task<IReadOnlyList<TcgplayerPrinting>> GetPrintingsAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetListAsync<TcgplayerPrinting>($"catalog/categories/{categoryId}/printings", cancellationToken);

    public Task<IReadOnlyList<TcgplayerLanguage>> GetLanguagesAsync(int categoryId, CancellationToken cancellationToken = default) =>
        GetListAsync<TcgplayerLanguage>($"catalog/categories/{categoryId}/languages", cancellationToken);

    /// <summary>Follows offset/limit paging until totalItems results are collected (or a page comes back empty).</summary>
    private async Task<IReadOnlyList<T>> GetAllPagesAsync<T>(string path, CancellationToken cancellationToken)
    {
        var separator = path.Contains('?') ? '&' : '?';
        var all = new List<T>();
        while (true)
        {
            var page = await SendAsync<T>($"{path}{separator}offset={all.Count}&limit={MaxPageSize}", cancellationToken);
            var results = page?.Results ?? [];
            all.AddRange(results);
            if (results.Count == 0 || page!.TotalItems is not { } total || all.Count >= total) break;
        }
        return all;
    }

    private async Task<IReadOnlyList<TResult>> GetBatchedAsync<TResult>(
        IEnumerable<int> ids, Func<string, string> path, CancellationToken cancellationToken)
    {
        var all = new List<TResult>();
        foreach (var batch in ids.Distinct().Chunk(MaxIdsPerRequest))
        {
            all.AddRange(await GetListAsync<TResult>(path(string.Join(',', batch)), cancellationToken));
        }
        return all;
    }

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string path, CancellationToken cancellationToken) =>
        (await SendAsync<T>(path, cancellationToken))?.Results ?? [];

    /// <summary>One request. Returns null when TCGplayer says nothing was found; throws on any other failure.</summary>
    private async Task<TcgplayerResponse<T>?> SendAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        TcgplayerResponse<T>? envelope = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body)) envelope = JsonSerializer.Deserialize<TcgplayerResponse<T>>(body, Json);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            // A non-JSON error page; reported by status below.
        }

        var status = (int)response.StatusCode;
        if (envelope is null)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new TcgplayerApiException($"TCGplayer GET {path} failed with HTTP {status}.", [], status);
            }
            throw new TcgplayerApiException($"TCGplayer GET {path} returned an empty body.", []);
        }

        var errors = envelope.Errors ?? [];
        if (envelope.Success && response.IsSuccessStatusCode) return envelope;
        if (IsNotFound(errors) && (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)) return null;

        var detail = errors.Count > 0 ? string.Join("; ", errors) : "no error details";
        throw new TcgplayerApiException(
            $"TCGplayer GET {path} failed (HTTP {status}): {detail}", errors,
            response.IsSuccessStatusCode ? null : status);
    }

    private static bool IsNotFound(IReadOnlyList<string> errors) =>
        errors.Count > 0 && errors.All(e => NotFoundErrors.Any(n => e.Contains(n, StringComparison.OrdinalIgnoreCase)));
}
