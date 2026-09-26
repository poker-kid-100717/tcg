using System.Text;
using System.Text.RegularExpressions;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>Maps TCGplayer printing names (pricing subTypeName) to this app's variant keys.</summary>
public static class TcgplayerVariants
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Normal"] = "normal",
        ["Holofoil"] = "holofoil",
        ["Reverse Holofoil"] = "reverseHolofoil",
        ["1st Edition Holofoil"] = "1stEditionHolofoil",
        ["1st Edition Normal"] = "1stEditionNormal",
        ["Unlimited Holofoil"] = "unlimitedHolofoil",
        ["Unlimited"] = "unlimited",
        ["1st Edition"] = "1stEdition",
    };

    /// <summary>
    /// "Reverse Holofoil" → "reverseHolofoil" and so on (the keys pokemontcg.io uses).
    /// Unknown names become camelCase: the first word lower-cased, later words
    /// capitalised, anything that is not a letter or digit dropped.
    /// </summary>
    public static string ToVariantKey(string subTypeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subTypeName);
        var trimmed = subTypeName.Trim();
        if (Known.TryGetValue(trimmed, out var key)) return key;

        var words = Regex.Split(trimmed, @"[^\p{L}\p{N}]+");
        var result = new StringBuilder();
        foreach (var word in words.Where(w => w.Length > 0))
        {
            if (result.Length == 0) result.Append(word.ToLowerInvariant());
            else result.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1).ToString().ToLowerInvariant());
        }
        return result.ToString();
    }
}
