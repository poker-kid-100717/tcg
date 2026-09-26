using System.Text.Json;

namespace PokemonTCG.API.Product;

public sealed class ScrydexOptions
{
    public const string SectionName = "Scrydex";

    public string BaseUrl { get; set; } = "https://api.scrydex.com/";
    public string ApiKey { get; set; } = "";
    public string TeamId { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(TeamId);
}

public sealed record ScrydexHistoryPoint(DateOnly Date, decimal? Low, decimal Market);
public sealed record ScrydexSoldComp(
    string Id,
    string Source,
    string? Title,
    decimal Price,
    string Currency,
    DateOnly SoldAt,
    string? Company,
    string? Grade,
    string? Url)
{
    public bool IsRaw => string.IsNullOrWhiteSpace(Company) && string.IsNullOrWhiteSpace(Grade);
}

public sealed record ScrydexMarketData(
    IReadOnlyList<ScrydexHistoryPoint> History,
    IReadOnlyList<ScrydexSoldComp> SoldComps);

/// <summary>
/// Optional premium provider. The legacy Pokémon TCG API remains the catalog/fallback
/// source during migration, while Scrydex enriches Pro intelligence with historical
/// prices and sold-listing provenance. All calls stay server-side.
/// </summary>
public sealed class ScrydexClient(HttpClient http, ScrydexOptions options, ILogger<ScrydexClient> logger)
{
    public bool IsConfigured => options.IsConfigured;

    public async Task<ScrydexMarketData?> GetMarketDataAsync(
        string cardId,
        string variant,
        int days,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;

        try
        {
            var historyTask = GetHistoryAsync(cardId, variant, days, cancellationToken);
            var listingsTask = GetListingsAsync(cardId, variant, Math.Min(days, 180), cancellationToken);
            await Task.WhenAll(historyTask, listingsTask);
            return new ScrydexMarketData(historyTask.Result, listingsTask.Result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Scrydex enrichment failed for {CardId} {Variant}; using local snapshots only", cardId, variant);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Scrydex returned an unexpected response for {CardId} {Variant}", cardId, variant);
            return null;
        }
    }

    private async Task<IReadOnlyList<ScrydexHistoryPoint>> GetHistoryAsync(
        string cardId,
        string variant,
        int days,
        CancellationToken cancellationToken)
    {
        var path =
            $"pokemon/v1/cards/{Uri.EscapeDataString(cardId)}/price_history" +
            $"?days={Math.Clamp(days, 7, 365)}" +
            $"&variant={Uri.EscapeDataString(variant)}" +
            "&condition=NM&page_size=365";

        using var document = await GetJsonAsync(path, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var points = new List<ScrydexHistoryPoint>();
        foreach (var day in data.EnumerateArray())
        {
            if (!TryDate(day, "date", out var date) ||
                !day.TryGetProperty("prices", out var prices) ||
                prices.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var price in prices.EnumerateArray())
            {
                if (!EqualsIgnoreCase(price, "variant", variant) ||
                    !EqualsIgnoreCase(price, "condition", "NM") ||
                    !EqualsIgnoreCase(price, "type", "raw") ||
                    !EqualsIgnoreCase(price, "currency", "USD") ||
                    !TryDecimal(price, "market", out var market))
                    continue;

                TryDecimal(price, "low", out var low);
                points.Add(new ScrydexHistoryPoint(date, low, market));
                break;
            }
        }

        return points.OrderBy(p => p.Date).ToList();
    }

    private async Task<IReadOnlyList<ScrydexSoldComp>> GetListingsAsync(
        string cardId,
        string variant,
        int days,
        CancellationToken cancellationToken)
    {
        var path =
            $"pokemon/v1/cards/{Uri.EscapeDataString(cardId)}/listings" +
            $"?days={Math.Clamp(days, 7, 365)}" +
            $"&variant={Uri.EscapeDataString(variant)}" +
            "&page=1&page_size=100";

        using var document = await GetJsonAsync(path, cancellationToken);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var comps = new List<ScrydexSoldComp>();
        foreach (var listing in data.EnumerateArray())
        {
            if (!TryDecimal(listing, "price", out var price) ||
                !TryDate(listing, "sold_at", out var soldAt))
                continue;

            var currency = GetString(listing, "currency") ?? "";
            if (!currency.Equals("USD", StringComparison.OrdinalIgnoreCase)) continue;

            comps.Add(new ScrydexSoldComp(
                GetString(listing, "id") ?? Guid.NewGuid().ToString("N"),
                GetString(listing, "source") ?? "unknown",
                GetString(listing, "title"),
                price,
                currency,
                soldAt,
                GetString(listing, "company"),
                GetString(listing, "grade"),
                GetString(listing, "url")));
        }

        return comps.OrderByDescending(c => c.SoldAt).ToList();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Api-Key", options.ApiKey);
        request.Headers.Add("X-Team-ID", options.TeamId);
        request.Headers.Add("Accept", "application/json");

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Scrydex returned {(int)response.StatusCode}.", null, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static bool EqualsIgnoreCase(JsonElement element, string property, string expected) =>
        GetString(element, property)?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryDecimal(JsonElement element, string property, out decimal value)
    {
        value = 0;
        return element.TryGetProperty(property, out var item) &&
               item.ValueKind == JsonValueKind.Number &&
               item.TryGetDecimal(out value);
    }

    private static bool TryDate(JsonElement element, string property, out DateOnly date)
    {
        date = default;
        var raw = GetString(element, property);
        return raw is not null &&
               (DateOnly.TryParse(raw, out date) ||
                DateOnly.TryParseExact(raw, "yyyy/MM/dd", out date));
    }
}
