using System.Globalization;

namespace PokemonTCG.API.Pricing.Predictions;

/// <summary>
/// One card printing on one day, as the feature query returns it: its prices then, its recent history,
/// where it sits in its set, the card's attributes, and (for training) its price <c>HorizonDays</c> later.
/// </summary>
public sealed class SampleRow
{
    public required string CardId { get; init; }
    public required string Variant { get; init; }
    public DateOnly Date { get; init; }
    public decimal Market { get; init; }
    public decimal? Low { get; init; }
    public decimal? Mid { get; init; }
    public decimal? High { get; init; }
    public decimal? Market7 { get; init; }
    public decimal? Market30 { get; init; }
    public decimal? Market90 { get; init; }
    public double? Volatility30 { get; init; }
    public double? Slope30 { get; init; }
    public double? Fit30 { get; init; }
    public decimal? Future { get; init; }
    public required string SetId { get; init; }
    public required string SetName { get; init; }
    public long SetRank { get; init; }
    public decimal SetShare { get; init; }
    public required string Number { get; init; }
    public string? Rarity { get; init; }
    public string? Supertype { get; init; }
    public string[]? Subtypes { get; init; }
    public int? NationalDex { get; init; }
    public string? Artist { get; init; }
    public DateOnly? SetReleased { get; init; }
    public int? SetPrintedTotal { get; init; }

    /// <summary>What the model predicts: the log of the price ratio over the horizon.</summary>
    public double? Label => Future is > 0 ? Math.Log((double)(Future.Value / Market)) : null;
}

/// <summary>How much more (or less) a group's cards sell for than the typical card on the same day.</summary>
public sealed record Premiums(
    IReadOnlyDictionary<string, double> Rarity,
    IReadOnlyDictionary<int, double> Pokemon,
    IReadOnlyDictionary<string, double> Artist);

/// <summary>Per-Pokémon facts that don't change day to day: a display name and how many cards feature it.</summary>
public sealed record Species(string Name, int Printings);

/// <summary>
/// The model's inputs. Each feature is a number the gradient-boosted trees can split on, and has a
/// sentence template so a prediction can say which features moved it and why.
/// </summary>
public static class PriceFeatures
{
    public const int Count = 27;

    public static readonly (string Name, string Label)[] All =
    [
        ("log_price", "Price level"),
        ("change_7d", "Change over 7 days"),
        ("change_30d", "Change over 30 days"),
        ("change_90d", "Change over 90 days"),
        ("has_90d_history", "Has 90 days of history"),
        ("volatility_30d", "Day-to-day volatility"),
        ("trend_30d", "30-day trend line"),
        ("trend_fit_30d", "Steadiness of the 30-day trend"),
        ("listing_gap", "Cheapest listing vs market"),
        ("mid_gap", "Median listing vs market"),
        ("high_spread", "Highest listing vs market"),
        ("set_age", "Set age"),
        ("set_size", "Set size"),
        ("secret_rare", "Secret rare"),
        ("set_rank", "Rank by value in its set"),
        ("set_share", "Share of the set's value"),
        ("rarity_premium", "Rarity"),
        ("pokemon_premium", "Pokémon popularity"),
        ("pokemon_printings", "Number of printings of the Pokémon"),
        ("artist_premium", "Artist"),
        ("trainer", "Trainer card"),
        ("energy", "Energy card"),
        ("rule_box", "Rule box (ex, V, GX…)"),
        ("holofoil", "Holofoil printing"),
        ("reverse_holo", "Reverse holo printing"),
        ("first_edition", "1st Edition printing"),
        ("release_year", "Era (release year)"),
    ];

    private static readonly HashSet<string> RuleBox = new(StringComparer.OrdinalIgnoreCase)
    {
        "ex", "EX", "GX", "V", "VMAX", "VSTAR", "V-UNION", "TAG TEAM", "BREAK", "LEGEND", "Radiant", "Prism Star", "MEGA", "Mega",
    };

    public static float[] Vector(SampleRow row, Premiums premiums, IReadOnlyDictionary<int, Species> species)
    {
        var price = (double)row.Market;
        var v = new float[Count];
        v[0] = (float)Math.Log(price);
        v[1] = LogChange(row.Market, row.Market7);
        v[2] = LogChange(row.Market, row.Market30);
        v[3] = LogChange(row.Market, row.Market90);
        v[4] = row.Market90 is null ? 0 : 1;
        v[5] = (float)(row.Volatility30 ?? 0);
        v[6] = (float)((row.Slope30 ?? 0) * 30);
        v[7] = (float)(row.Fit30 ?? 0);
        v[8] = LogChange(row.Low, row.Market);
        v[9] = LogChange(row.Mid, row.Market);
        v[10] = LogChange(row.High, row.Market);
        v[11] = row.SetReleased is { } released ? (row.Date.DayNumber - released.DayNumber) / 365.25f : 0;
        v[12] = row.SetPrintedTotal ?? 0;
        v[13] = IsSecret(row) ? 1 : 0;
        v[14] = (float)Math.Log(row.SetRank);
        v[15] = (float)row.SetShare;
        v[16] = (float)(row.Rarity is { } r && premiums.Rarity.TryGetValue(r, out var rp) ? rp : 0);
        v[17] = (float)(row.NationalDex is { } dex && premiums.Pokemon.TryGetValue(dex, out var pp) ? pp : 0);
        v[18] = (float)Math.Log(1 + (row.NationalDex is { } d && species.TryGetValue(d, out var s) ? s.Printings : 0));
        v[19] = (float)(row.Artist is { } a && premiums.Artist.TryGetValue(a, out var ap) ? ap : 0);
        v[20] = row.Supertype == "Trainer" ? 1 : 0;
        v[21] = row.Supertype == "Energy" ? 1 : 0;
        v[22] = row.Subtypes?.Any(RuleBox.Contains) == true ? 1 : 0;
        v[23] = row.Variant.Contains("olofoil") && !row.Variant.StartsWith("reverse") ? 1 : 0;
        v[24] = row.Variant.StartsWith("reverse") ? 1 : 0;
        v[25] = row.Variant.StartsWith("1stEdition") ? 1 : 0;
        v[26] = row.SetReleased?.Year ?? 0;
        return v;
    }

    /// <summary>A plain-English line for a feature's value on this card, to explain a prediction.</summary>
    public static string Describe(int feature, SampleRow row, float value, IReadOnlyDictionary<int, Species> species)
    {
        string Ratio(float logRatio) => Math.Exp(logRatio).ToString(Math.Exp(logRatio) >= 10 ? "0" : "0.0", CultureInfo.InvariantCulture) + "×";
        string Change(float logChange) => Percent(Math.Exp(logChange) - 1);
        var pokemon = row.NationalDex is { } dex && species.TryGetValue(dex, out var s) ? s : null;

        return feature switch
        {
            0 => $"Current price of {row.Market.ToString("C2", CultureInfo.GetCultureInfo("en-US"))}",
            1 => $"{Change(value)} over the last 7 days",
            2 => $"{Change(value)} over the last 30 days",
            3 => row.Market90 is null ? "Less than 90 days of price history" : $"{Change(value)} over the last 90 days",
            4 => value > 0 ? "At least 90 days of price history" : "Less than 90 days of price history",
            5 => $"Day-to-day price swings of about {value * 100:0.#}%",
            6 => $"30-day trend line of {Change(value)}",
            7 => value >= 0.6 ? "A steady 30-day trend" : "An uneven 30-day trend",
            8 => row.Low is null ? "No listing price" : $"Cheapest listing {Relative(value)} the market price",
            9 => row.Mid is null ? "No median listing price" : $"Median listing {Relative(value)} the market price",
            10 => row.High is null ? "No highest listing price" : $"Highest listing {Relative(value)} the market price",
            11 => row.SetReleased is null ? "Unknown set release date" : $"{row.SetName} released {Age(value)} ago",
            12 => $"{row.SetName} has {value:0} cards",
            13 => value > 0 ? $"Secret rare, numbered past the set's {row.SetPrintedTotal}" : "Numbered within the main set",
            14 => $"#{row.SetRank} most valuable card in {row.SetName}",
            15 => $"{row.SetShare * 100:0.#}% of {row.SetName}'s total value",
            16 => $"{row.Rarity ?? "Unknown rarity"} cards sell for {Ratio(value)} the typical card",
            17 => pokemon is null ? "Not a Pokémon card" : $"{pokemon.Name} cards sell for {Ratio(value)} the typical card",
            18 => pokemon is null ? "Not a Pokémon card" : $"{pokemon.Name} appears on {pokemon.Printings} cards",
            19 => row.Artist is null ? "Unknown artist" : $"Art by {row.Artist}, whose cards sell for {Ratio(value)} the typical card",
            20 => value > 0 ? "Trainer card" : "Not a Trainer card",
            21 => value > 0 ? "Energy card" : "Not an Energy card",
            22 => value > 0 ? $"Rule box card ({string.Join(", ", row.Subtypes!.Where(RuleBox.Contains))})" : "No rule box",
            23 or 24 or 25 => $"{Prices.VariantLabel(row.Variant)} printing",
            26 => row.SetReleased is null ? "Unknown era" : $"Released in {value:0}",
            _ => All[feature].Label,
        };
    }

    private static float LogChange(decimal? now, decimal? then) =>
        now is > 0 && then is > 0 ? (float)Math.Log((double)(now.Value / then.Value)) : 0;

    private static bool IsSecret(SampleRow row) =>
        row.SetPrintedTotal is > 0 && int.TryParse(new string(row.Number.TakeWhile(char.IsDigit).ToArray()), out var n) && n > row.SetPrintedTotal;

    private static string Percent(double change) => $"{(change >= 0 ? "+" : "")}{change * 100:0.#}%";

    private static string Relative(float logRatio)
    {
        var change = Math.Exp(logRatio) - 1;
        return Math.Abs(change) < 0.005 ? "at" : $"{Math.Abs(change) * 100:0.#}% {(change > 0 ? "above" : "below")}";
    }

    private static string Age(float years) =>
        years < 1 ? $"{Math.Max(1, (int)Math.Round(years * 12))} months" : $"{years:0.#} years";

    /// <summary>
    /// For each group (a rarity, a Pokémon, an artist): how its cards' median price compares with the median
    /// card that day, as a log ratio, shrunk toward zero for small groups so three cards can't make an artist
    /// look like a star.
    /// </summary>
    public static Premiums ComputePremiums(IEnumerable<SampleRow> sameDay)
    {
        var rows = sameDay.ToList();
        if (rows.Count == 0) return new Premiums(new Dictionary<string, double>(), new Dictionary<int, double>(), new Dictionary<string, double>());
        var overall = Median(rows.Select(r => Math.Log((double)r.Market)));

        Dictionary<TKey, double> Premium<TKey>(IEnumerable<(TKey Key, SampleRow Row)> keyed) where TKey : notnull =>
            keyed.GroupBy(k => k.Key, k => k.Row)
                .ToDictionary(g => g.Key, g =>
                {
                    var n = g.Count();
                    return n / (n + 5.0) * (Median(g.Select(r => Math.Log((double)r.Market))) - overall);
                });

        return new Premiums(
            Premium(rows.Where(r => r.Rarity is not null).Select(r => (r.Rarity!, r))),
            Premium(rows.Where(r => r.NationalDex is not null).Select(r => (r.NationalDex!.Value, r))),
            Premium(rows.Where(r => r.Artist is not null).Select(r => (r.Artist!, r))));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
