namespace PokemonTCG.API.Pricing;

public static class Prices
{
    /// <summary>
    /// The printing a card's headline price comes from. A holo rare's price is
    /// its holofoil printing, a common's is its normal printing; reverse holos
    /// and first editions are only used when nothing else is listed.
    /// </summary>
    private static readonly string[] VariantPreference =
    [
        "holofoil", "normal", "1stEditionHolofoil", "1stEditionNormal", "unlimitedHolofoil", "unlimited", "reverseHolofoil",
    ];

    public static string? HeadlineVariant(IReadOnlyDictionary<string, TcgPrice>? prices)
    {
        if (prices is null || prices.Count == 0) return null;
        return VariantPreference.FirstOrDefault(v => prices.TryGetValue(v, out var p) && p.Market is not null)
               ?? prices.Where(p => p.Value.Market is not null).Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
    }

    public static decimal? HeadlinePrice(IReadOnlyDictionary<string, TcgPrice>? prices) =>
        HeadlineVariant(prices) is { } variant ? prices![variant].Market : null;

    /// <summary>Order in which to list a card's printings.</summary>
    public static int VariantOrder(string variant)
    {
        var index = Array.IndexOf(VariantPreference, variant);
        return index < 0 ? VariantPreference.Length : index;
    }

    /// <summary>"reverseHolofoil" → "Reverse Holofoil", "1stEditionNormal" → "1st Edition Normal".</summary>
    public static string VariantLabel(string variant)
    {
        var words = System.Text.RegularExpressions.Regex.Replace(variant, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return char.ToUpperInvariant(words[0]) + words[1..];
    }
}
