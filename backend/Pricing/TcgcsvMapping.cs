using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PokemonTCG.API.Pricing;

/// <summary>
/// Joins TCGCSV (TCGplayer) data onto the card database the site is built on: groups to sets, product
/// numbers to card ids, and printing names to the variant keys used everywhere else.
/// </summary>
public static partial class TcgcsvMapping
{
    /// <summary>
    /// Pairs each TCGCSV group with at most one set, and each set with at most one group. A pair needs
    /// real evidence: an exact name (4), the set code (2), the release date (2), or a name that ends with
    /// (2) or contains (1) the set's. Pairs need 4 and the strongest are taken first, so a sub-set such as
    /// a "Classic Collection" can't claim its parent set's cards, and an unclear group is left unpaired
    /// rather than risk recording one set's prices under another's cards.
    /// </summary>
    public static IReadOnlyDictionary<int, TcgSet> MatchSets(IReadOnlyList<TcgcsvGroup> groups, IReadOnlyList<TcgSet> sets)
    {
        var candidates =
            from g in groups
            from s in sets
            let score = Score(g, s)
            where score >= 4
            orderby score descending, g.GroupId
            select (Group: g, Set: s, Score: score);

        var matched = new Dictionary<int, TcgSet>();
        var usedSets = new HashSet<string>();
        foreach (var (group, set, _) in candidates)
        {
            if (matched.ContainsKey(group.GroupId) || usedSets.Contains(set.Id)) continue;
            matched[group.GroupId] = set;
            usedSets.Add(set.Id);
        }
        return matched;
    }

    internal static int Score(TcgcsvGroup group, TcgSet set)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(group.Abbreviation) &&
            string.Equals(group.Abbreviation, set.PtcgoCode, StringComparison.OrdinalIgnoreCase))
            score += 2;

        if (group.Published is { } published && set.Released is { } released && published == released)
            score += 2;

        var groupName = NormalizeName(StripSeriesPrefix(group.Name));
        var setName = NormalizeName(set.Name);
        if (setName.Length > 0)
        {
            if (groupName == setName) score += 4;
            else if (groupName.EndsWith(setName, StringComparison.Ordinal)) score += 2;
            else if (groupName.Contains(setName, StringComparison.Ordinal)) score += 1;
        }
        return score;
    }

    /// <summary>"SV: Scarlet &amp; Violet 151" → "Scarlet &amp; Violet 151"; "SWSH12: Silver Tempest" → "Silver Tempest".</summary>
    internal static string StripSeriesPrefix(string name) => SeriesPrefix().Replace(name, "");

    internal static string NormalizeName(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // é → e
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>
    /// The comparable form of a card number: the part before any "/", upper-cased, without leading zeros
    /// in its digit runs. "006/165" and "6" both become "6"; "TG01/TG30" and "TG01" both become "TG1".
    /// </summary>
    public static string NormalizeNumber(string number)
    {
        var slash = number.IndexOf('/');
        var printed = (slash >= 0 ? number[..slash] : number).Trim().ToUpperInvariant();
        return LeadingZeros().Replace(printed, "");
    }

    /// <summary>TCGplayer printing names to the site's variant keys: "Reverse Holofoil" → "reverseHolofoil".</summary>
    public static string VariantKey(string? subTypeName)
    {
        if (string.IsNullOrWhiteSpace(subTypeName)) return "normal";
        var words = subTypeName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var builder = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            builder.Append(i == 0
                ? word.ToLowerInvariant()
                : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"^[A-Za-z]{1,6}\d{0,3}[a-z]?\s*:\s*")]
    private static partial Regex SeriesPrefix();

    // A run of zeros at the start of the number or right after a letter, when more digits follow.
    [GeneratedRegex(@"(?<=^|[A-Z])0+(?=\d)")]
    private static partial Regex LeadingZeros();
}

/// <summary>What kind of sealed product a TCGplayer listing is, and its usual US MSRP.</summary>
public static class SealedProducts
{
    /// <summary>
    /// The Scarlet &amp; Violet era began on 2023-03-31. Typical MSRPs are only applied from then on:
    /// earlier products had different retail prices, and we'd rather show none than a wrong one.
    /// </summary>
    public static readonly DateOnly TypicalMsrpEraStart = new(2023, 3, 31);

    // Standard US MSRPs for the current era. A booster pack is $4.49; a 6-pack booster bundle is 6 × $4.49
    // = $26.94; a 36-pack booster box is 36 × $4.49 = $161.64. Elite Trainer Boxes are $49.99, Pokémon
    // Center ETBs $59.99 and mini tins $9.99 (The Pokémon Company's published prices, e.g. for
    // 30th Celebration). A few special-set ETBs list higher; set sealed_products.msrp to override.
    private static readonly Dictionary<string, decimal> Typical = new()
    {
        ["Elite Trainer Box"] = 49.99m,
        ["Pokémon Center ETB"] = 59.99m,
        ["Booster Bundle"] = 26.94m,
        ["Booster Box"] = 161.64m,
        ["Booster Pack"] = 4.49m,
        ["Mini Tin"] = 9.99m,
    };

    public static decimal? TypicalMsrp(string productType, DateOnly? released) =>
        released is { } date && date >= TypicalMsrpEraStart && Typical.TryGetValue(productType, out var msrp) ? msrp : null;

    /// <summary>The product type, or null for listings that aren't sealed product collectors buy (code cards, accessories).</summary>
    public static string? Classify(string name)
    {
        var n = name.ToLowerInvariant();
        bool Has(string term) => n.Contains(term, StringComparison.Ordinal);
        var bulk = Has(" case") || Has("display");

        if (Has("code card") || Has("online code") || Has("sleeves") || Has("deck box") || Has("playmat") ||
            (Has("binder") && !Has("collection")))
            return null;
        // Store-exclusive multi-item bundles ("Costco 2-Pack Trainer Box and Booster Bundle", "ETB and
        // Pokeball (Sam's Club)") have no standard MSRP and mustn't be compared with a single item's.
        if (Has("costco") || Has("sam's club") || Has("2-pack") || Has("2 pack") ||
            (Has(" and ") && !Has("build and battle")))
            return "Retail Bundle";
        if (Has("elite trainer box"))
            return bulk ? "ETB Case" : Has("pokemon center") || Has("pokémon center") ? "Pokémon Center ETB" : "Elite Trainer Box";
        if (Has("booster bundle")) return bulk ? "Booster Bundle Case" : "Booster Bundle";
        if (Has("booster box") || Has("booster display")) return Has(" case") ? "Booster Box Case" : "Booster Box";
        if (Has("ultra premium collection") || Has("ultra-premium collection")) return "Ultra-Premium Collection";
        if (Has("mini tin")) return bulk ? "Mini Tin Display" : "Mini Tin";
        if (Has("build & battle") || Has("build and battle")) return bulk ? "Build & Battle Display" : "Build & Battle Box";
        if (Has("blister")) return "Blister";
        if (Has("booster pack") || Has("sleeved booster"))
            return Has("art bundle") ? "Booster Pack Art Bundle" : bulk ? "Booster Pack Display" : "Booster Pack";
        if (Has(" tin")) return "Tin";
        if (Has("collection") || Has(" box") || Has("chest") || Has("bundle")) return bulk ? "Case" : "Collection";
        if (bulk) return "Case";
        return null;
    }
}
