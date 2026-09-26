namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>A card's condition grade, as TCGplayer sells it.</summary>
public enum CardCondition
{
    NearMint,
    LightlyPlayed,
    ModeratelyPlayed,
    HeavilyPlayed,
    Damaged,
}

/// <summary>Maps TCGplayer condition names to <see cref="CardCondition"/>.</summary>
public static class TcgplayerConditions
{
    private static readonly Dictionary<string, CardCondition> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Near Mint"] = CardCondition.NearMint,
        ["NM"] = CardCondition.NearMint,
        ["Lightly Played"] = CardCondition.LightlyPlayed,
        ["LP"] = CardCondition.LightlyPlayed,
        ["Moderately Played"] = CardCondition.ModeratelyPlayed,
        ["MP"] = CardCondition.ModeratelyPlayed,
        ["Heavily Played"] = CardCondition.HeavilyPlayed,
        ["HP"] = CardCondition.HeavilyPlayed,
        ["Damaged"] = CardCondition.Damaged,
        ["DMG"] = CardCondition.Damaged,
    };

    // Some categories name conditions per finish ("Near Mint Foil", "Lightly Played Holofoil").
    private static readonly string[] Suffixes = [" Holofoil", " Foil"];

    /// <summary>
    /// "Near Mint", "Lightly Played Holofoil", "Damaged Foil", "NM"... →
    /// the condition; false for anything else (e.g. "Unopened").
    /// </summary>
    public static bool TryParse(string? name, out CardCondition condition)
    {
        condition = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        var trimmed = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        foreach (var suffix in Suffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed[..^suffix.Length];
                break;
            }
        }
        return Names.TryGetValue(trimmed, out condition);
    }

    /// <summary>The condition for <paramref name="name"/>, or null if it is not one of the five grades.</summary>
    public static CardCondition? ToCondition(string? name) => TryParse(name, out var c) ? c : null;
}
