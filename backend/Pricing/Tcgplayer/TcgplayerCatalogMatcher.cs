using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>One of our sets (pokemontcg.io ids), as the matcher needs it.</summary>
public record CatalogSet(string Id, string Name, string? Series, DateOnly? ReleaseDate, int PrintedTotal, int Total);

/// <summary>One of our cards: id like "sv3pt5-6", number as printed ("6", "TG05", "SWSH101").</summary>
public record CatalogCard(string Id, string SetId, string Number, string Name);

public enum CatalogItemKind { Set, Card }

public enum SetMatchMethod
{
    /// <summary>Normalized names are equal.</summary>
    ExactName,
    /// <summary>Published within the date window with the best name-token overlap.</summary>
    ReleaseDate,
    /// <summary>Our set is a subset (e.g. "Brilliant Stars Trainer Gallery") that TCGplayer lists inside its parent's group.</summary>
    Subset,
}

public record SetGroupMatch(string SetId, int GroupId, SetMatchMethod Method);

/// <summary>Something we could not match, or matched ambiguously, and why.</summary>
public record UnmatchedCatalogItem(CatalogItemKind Kind, string Id, string Reason);

public record TcgplayerCatalogMatch(
    IReadOnlyDictionary<string, int> SetToGroup,
    IReadOnlyDictionary<string, int> CardToProduct,
    IReadOnlyList<SetGroupMatch> SetMatches,
    IReadOnlyList<UnmatchedCatalogItem> Unmatched);

/// <summary>
/// Links our catalog (pokemontcg.io sets and cards) to TCGplayer groups and
/// products. Prefers precision over recall: anything ambiguous is left
/// unmatched and reported rather than guessed.
/// </summary>
/// <remarks>
/// Sets, in order:
/// <list type="number">
/// <item>Exact match on normalized names (case, accents, punctuation, "&amp;"/"and",
/// leading series codes such as "SV03.5:", "SWSH07:", "SM -", and the word "Pokémon" ignored).
/// Several groups with the same name are narrowed by the date window.</item>
/// <item>Subset: our "&lt;Parent&gt; Trainer Gallery" / "Galarian Gallery" / "Shiny Vault"
/// sets, when no group of their own exists, share the parent's group — TCGplayer
/// lists e.g. Brilliant Stars' TG cards inside the Brilliant Stars group. This is
/// the only case where two of our sets map to one group; the card numbers
/// ("TG05" vs "5") keep their cards apart.</item>
/// <item>Date fallback: unclaimed groups published within ±<see cref="DateWindowDays"/> days
/// of our release date, scored by name-token overlap (at least <see cref="MinTokenOverlap"/>).
/// A tie for best, or two sets wanting one group with equal scores, is reported as ambiguous.</item>
/// </list>
/// Cards match within their set's group on the collector number (the part before "/",
/// leading zeros dropped: "006/165" → "6", "TG05/TG30" → "TG5") and a loosely equal
/// name (TCGplayer's " - 006/165" suffixes and parenthesized notes such as "(Full Art)" ignored).
/// </remarks>
public static partial class TcgplayerCatalogMatcher
{
    public const int DateWindowDays = 10;
    public const double MinTokenOverlap = 0.5;

    private static readonly string[] SubsetSuffixes = ["trainer gallery", "galarian gallery", "shiny vault"];
    private static readonly HashSet<string> StopWords = ["and", "the", "of", "a", "set", "card"];

    public static TcgplayerCatalogMatch Match(
        IEnumerable<CatalogSet> sets,
        IEnumerable<CatalogCard> cards,
        IEnumerable<TcgplayerGroup> groups,
        IEnumerable<TcgplayerProduct> products)
    {
        var setList = sets.ToList();
        var groupList = groups.ToList();
        var unmatched = new List<UnmatchedCatalogItem>();

        var setMatches = MatchSets(setList, groupList, unmatched);
        var setToGroup = setMatches.ToDictionary(m => m.SetId, m => m.GroupId);
        var cardToProduct = MatchCards(cards, setToGroup, products, unmatched);

        return new TcgplayerCatalogMatch(setToGroup, cardToProduct, setMatches, unmatched);
    }

    // ---------------------------------------------------------------- sets

    private static List<SetGroupMatch> MatchSets(List<CatalogSet> sets, List<TcgplayerGroup> groups, List<UnmatchedCatalogItem> unmatched)
    {
        var matches = new Dictionary<string, SetGroupMatch>();
        var claimed = new HashSet<int>();
        var failed = new Dictionary<string, string>(); // setId → reason (final unless matched later)
        var setNames = sets.ToDictionary(s => s.Id, s => NormalizeSetName(s.Name));
        var groupNames = groups.ToDictionary(g => g.GroupId, g => NormalizeSetName(g.Name));

        // 1. Exact normalized name.
        var exactProposals = new List<(CatalogSet Set, TcgplayerGroup Group)>();
        foreach (var set in sets)
        {
            var candidates = groups.Where(g => groupNames[g.GroupId] == setNames[set.Id]).ToList();
            if (candidates.Count > 1)
            {
                var inWindow = candidates.Where(g => WithinWindow(set, g)).ToList();
                if (inWindow.Count != 1)
                {
                    failed[set.Id] = $"ambiguous: {candidates.Count} TCGplayer groups named like \"{set.Name}\" ({Describe(candidates)})";
                    continue;
                }
                candidates = inWindow;
            }
            if (candidates.Count == 1) exactProposals.Add((set, candidates[0]));
        }
        foreach (var byGroup in exactProposals.GroupBy(p => p.Group.GroupId))
        {
            var proposals = byGroup.ToList();
            if (proposals.Count == 1)
            {
                Accept(proposals[0].Set, proposals[0].Group, SetMatchMethod.ExactName);
                continue;
            }
            // Several of our sets normalize to one group name: only the one in the date window, if exactly one is.
            var inWindow = proposals.Where(p => WithinWindow(p.Set, p.Group)).ToList();
            if (inWindow.Count == 1) Accept(inWindow[0].Set, inWindow[0].Group, SetMatchMethod.ExactName);
            foreach (var p in proposals.Where(p => inWindow.Count != 1 || p.Set != inWindow[0].Set))
            {
                failed[p.Set.Id] = $"ambiguous: TCGplayer group {p.Group.GroupId} \"{p.Group.Name}\" matches several of our sets by name "
                                   + $"({string.Join(", ", proposals.Select(x => x.Set.Id))})";
            }
        }

        // 2. Subsets that TCGplayer folds into the parent group (again after step 3,
        //    in case the parent only matched by date).
        MatchSubsets();

        // 3. Release-date window + token overlap.
        var dateProposals = new List<(CatalogSet Set, TcgplayerGroup Group, double Score, double Jaccard)>();
        foreach (var set in sets.Where(s => !matches.ContainsKey(s.Id) && !failed.ContainsKey(s.Id) && SubsetParentName(s) is null))
        {
            if (set.ReleaseDate is null)
            {
                failed[set.Id] = "no TCGplayer group with the same name, and no release date to match on";
                continue;
            }
            var setTokens = Tokens(setNames[set.Id]);
            var scored = groups
                .Where(g => !claimed.Contains(g.GroupId) && WithinWindow(set, g))
                .Select(g => (Group: g, Score: Overlap(setTokens, Tokens(groupNames[g.GroupId]))))
                .Where(x => x.Score.Coefficient >= MinTokenOverlap)
                .OrderByDescending(x => x.Score.Coefficient).ThenByDescending(x => x.Score.Jaccard)
                .ToList();
            if (scored.Count == 0)
            {
                failed[set.Id] = $"no TCGplayer group with the same name, or published within {DateWindowDays} days with a similar name";
                continue;
            }
            if (scored.Count > 1 && scored[1].Score == scored[0].Score)
            {
                var tied = scored.TakeWhile(x => x.Score == scored[0].Score).Select(x => x.Group).ToList();
                failed[set.Id] = $"ambiguous: equally good TCGplayer groups by date and name ({Describe(tied)})";
                continue;
            }
            dateProposals.Add((set, scored[0].Group, scored[0].Score.Coefficient, scored[0].Score.Jaccard));
        }
        foreach (var byGroup in dateProposals.GroupBy(p => p.Group.GroupId))
        {
            var ranked = byGroup.OrderByDescending(p => p.Score).ThenByDescending(p => p.Jaccard).ToList();
            var winner = ranked[0];
            var tie = ranked.Count > 1 && ranked[1].Score == winner.Score && ranked[1].Jaccard == winner.Jaccard;
            if (!tie) Accept(winner.Set, winner.Group, SetMatchMethod.ReleaseDate);
            foreach (var p in ranked.Skip(tie ? 0 : 1))
            {
                failed[p.Set.Id] = tie
                    ? $"ambiguous: TCGplayer group {p.Group.GroupId} \"{p.Group.Name}\" fits several of our sets equally ({string.Join(", ", ranked.Select(r => r.Set.Id))})"
                    : $"best TCGplayer group {p.Group.GroupId} \"{p.Group.Name}\" is a better fit for set {winner.Set.Id}";
            }
        }

        MatchSubsets();

        foreach (var set in sets.Where(s => !matches.ContainsKey(s.Id)))
        {
            unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Set, set.Id,
                failed.GetValueOrDefault(set.Id) ?? "no matching TCGplayer group (for a subset set: its parent set was not matched)"));
        }
        return sets.Where(s => matches.ContainsKey(s.Id)).Select(s => matches[s.Id]).ToList();

        // "brilliant stars trainer gallery" → "brilliant stars", when we also have a set by that name.
        string? SubsetParentName(CatalogSet set)
        {
            var name = setNames[set.Id];
            var suffix = SubsetSuffixes.FirstOrDefault(s => name.EndsWith(" " + s));
            if (suffix is null) return null;
            var parent = name[..^(suffix.Length + 1)];
            return sets.Any(s => s.Id != set.Id && setNames[s.Id] == parent) ? parent : null;
        }

        void MatchSubsets()
        {
            foreach (var set in sets.Where(s => !matches.ContainsKey(s.Id) && !failed.ContainsKey(s.Id)))
            {
                if (SubsetParentName(set) is not { } parentName) continue;
                var parents = matches.Values
                    .Where(m => m.Method != SetMatchMethod.Subset && setNames[m.SetId] == parentName)
                    .ToList();
                if (parents.Count == 1) matches[set.Id] = new SetGroupMatch(set.Id, parents[0].GroupId, SetMatchMethod.Subset);
            }
        }

        void Accept(CatalogSet set, TcgplayerGroup group, SetMatchMethod method)
        {
            matches[set.Id] = new SetGroupMatch(set.Id, group.GroupId, method);
            claimed.Add(group.GroupId);
        }
    }

    private static bool WithinWindow(CatalogSet set, TcgplayerGroup group) =>
        set.ReleaseDate is { } released && group.PublishedOn is { } published
        && Math.Abs(DateOnly.FromDateTime(published).DayNumber - released.DayNumber) <= DateWindowDays;

    private static string Describe(IEnumerable<TcgplayerGroup> groups) =>
        string.Join(", ", groups.Select(g => $"{g.GroupId} \"{g.Name}\""));

    /// <summary>
    /// Name tokens for the date fallback: stop words dropped, series abbreviations
    /// expanded ("swsh" → "sword", "shield") and plurals folded ("promos" → "promo"),
    /// so "SWSH Black Star Promos" and "SWSH: Sword &amp; Shield Promo Cards" overlap.
    /// </summary>
    private static HashSet<string> Tokens(string normalized) =>
        normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(t => SeriesAbbreviations.TryGetValue(t, out var words) ? words : [t])
            .Select(t => t.Length > 3 && t.EndsWith('s') && !t.EndsWith("ss") ? t[..^1] : t)
            .Where(t => !StopWords.Contains(t))
            .ToHashSet();

    private static readonly Dictionary<string, string[]> SeriesAbbreviations = new()
    {
        ["sv"] = ["scarlet", "violet"],
        ["swsh"] = ["sword", "shield"],
        ["sm"] = ["sun", "moon"],
        ["bw"] = ["black", "white"],
        ["dp"] = ["diamond", "pearl"],
        ["hgss"] = ["heartgold", "soulsilver"],
    };

    /// <summary>Overlap coefficient (shared / smaller set) and Jaccard (shared / union) of two token sets.</summary>
    private static (double Coefficient, double Jaccard) Overlap(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return (0, 0);
        var shared = a.Count(b.Contains);
        return ((double)shared / Math.Min(a.Count, b.Count), (double)shared / (a.Count + b.Count - shared));
    }

    /// <summary>
    /// "SV03.5: Scarlet &amp; Violet 151" → "scarlet and violet 151";
    /// "SM - Guardians Rising" → "guardians rising"; "Pokémon GO" → "go".
    /// </summary>
    public static string NormalizeSetName(string name)
    {
        var stripped = SeriesCodePrefix().Replace(name.Trim(), "");
        var text = RemoveDiacritics(stripped).ToLowerInvariant().Replace("&", " and ");
        text = NonAlphanumeric().Replace(text, " ");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "pokemon");
        return string.Join(' ', words);
    }

    // Upper-case code, optional number (with ".5"-style decimals and a letter), then ":" or a dash.
    [GeneratedRegex(@"^[A-Z]{1,5}\d{0,3}(?:\.\d+)?[A-Za-z]?\s*[:\-–—]\s*")]
    private static partial Regex SeriesCodePrefix();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    // --------------------------------------------------------------- cards

    private static Dictionary<string, int> MatchCards(
        IEnumerable<CatalogCard> cards,
        Dictionary<string, int> setToGroup,
        IEnumerable<TcgplayerProduct> products,
        List<UnmatchedCatalogItem> unmatched)
    {
        var byGroupAndNumber = products
            .Select(p => (Product: p, Number: p.Number is { } n ? NormalizeNumber(n) : null))
            .Where(x => !string.IsNullOrEmpty(x.Number))
            .ToLookup(x => (x.Product.GroupId, x.Number!), x => x.Product);

        var proposals = new List<(CatalogCard Card, TcgplayerProduct Product)>();
        foreach (var card in cards)
        {
            if (!setToGroup.TryGetValue(card.SetId, out var groupId))
            {
                unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Card, card.Id, $"set {card.SetId} is not matched to a TCGplayer group"));
                continue;
            }

            var number = NormalizeNumber(card.Number);
            var sameNumber = byGroupAndNumber[(groupId, number)].ToList();
            if (sameNumber.Count == 0)
            {
                unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Card, card.Id, $"no product numbered {card.Number} in TCGplayer group {groupId}"));
                continue;
            }

            var key = NameKey(card.Name, stripParentheses: true);
            var named = sameNumber.Where(p => NameKey(p.Name, stripParentheses: true) == key).ToList();
            if (named.Count == 0)
            {
                unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Card, card.Id,
                    $"name mismatch: our \"{card.Name}\" vs TCGplayer {string.Join(", ", sameNumber.Select(p => $"{p.ProductId} \"{p.Name}\""))} (number {card.Number})"));
                continue;
            }
            if (named.Count > 1)
            {
                // e.g. "Pikachu" and "Pikachu (Cosmos Holo)" share a number: take the one whose full name is ours.
                var strictKey = NameKey(card.Name, stripParentheses: false);
                var strict = named.Where(p => NameKey(p.Name, stripParentheses: false) == strictKey).ToList();
                if (strict.Count != 1)
                {
                    unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Card, card.Id,
                        $"ambiguous: several TCGplayer products numbered {card.Number} named like \"{card.Name}\" ({string.Join(", ", named.Select(p => $"{p.ProductId} \"{p.Name}\""))})"));
                    continue;
                }
                named = strict;
            }
            proposals.Add((card, named[0]));
        }

        var result = new Dictionary<string, int>();
        foreach (var byProduct in proposals.GroupBy(p => p.Product.ProductId))
        {
            var claimants = byProduct.ToList();
            if (claimants.Count == 1)
            {
                result[claimants[0].Card.Id] = byProduct.Key;
                continue;
            }
            foreach (var c in claimants)
            {
                unmatched.Add(new UnmatchedCatalogItem(CatalogItemKind.Card, c.Card.Id,
                    $"ambiguous: TCGplayer product {byProduct.Key} \"{c.Product.Name}\" matches several of our cards ({string.Join(", ", claimants.Select(x => x.Card.Id))})"));
            }
        }
        return result;
    }

    /// <summary>"006/165" → "6"; "TG05/TG30" → "TG5"; "SWSH101" → "SWSH101"; "sv001" → "SV1".</summary>
    public static string NormalizeNumber(string number)
    {
        var head = number.Split('/')[0];
        var compact = Whitespace().Replace(head, "").ToUpperInvariant();
        return LeadingZeros().Replace(compact, "");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // Zeros at the start of a run of digits, keeping the last digit ("000" → "0").
    [GeneratedRegex(@"(?<!\d)0+(?=\d)")]
    private static partial Regex LeadingZeros();

    /// <summary>
    /// A comparison key for card names: accents, case, punctuation and spaces
    /// ignored; TCGplayer's " - 006/165" number suffix dropped; parenthesized or
    /// bracketed notes such as "(Full Art)" dropped when <paramref name="stripParentheses"/>.
    /// "Charizard ex - 006/165" and "Charizard ex" both → "charizardex".
    /// </summary>
    public static string NameKey(string name, bool stripParentheses = true)
    {
        var text = name;
        if (stripParentheses) text = Parenthesized().Replace(text, " ");
        text = NumberSuffix().Replace(text.Trim(), "");
        text = RemoveDiacritics(text).ToLowerInvariant()
            .Replace("&", "and").Replace("♀", "f").Replace("♂", "m");
        return NonAlphanumeric().Replace(text, "");
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Parenthesized();

    // " - 006/165", " - SWSH101", " - TG05/TG30" at the end of a name (a digit is required,
    // so hyphenated names like "Ho-Oh" and "Porygon-Z" are untouched).
    [GeneratedRegex(@"\s+-\s+[A-Za-z]*\d[\w/]*\s*$")]
    private static partial Regex NumberSuffix();

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
