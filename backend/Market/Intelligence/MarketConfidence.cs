namespace PokemonTCG.API.Market.Intelligence;

public enum ConfidenceBand
{
    High,
    Medium,
    Low,
    InsufficientData,
}

/// <summary>One input to the confidence score and what it cost.</summary>
public record ConfidenceFactor(string Name, int Penalty, string Detail);

/// <summary>
/// How far to trust a printing's current market reference. Deterministic: a fixed table of penalties from defensible
/// inputs (recency, observations, volatility, listing spread, range, outliers, source agreement, history), so the same
/// data always gives the same score and the same explanation.
/// </summary>
public record MarketConfidence(int? Score, ConfidenceBand Band, string Explanation, IReadOnlyList<ConfidenceFactor> Factors)
{
    /// <summary>Beyond this age, prices are too old to score at all.</summary>
    public const int MaxAgeDays = 14;
    public const int MinObservations = 5;

    /// <summary>A second source's price for the same printing (e.g. the median of verified sales).</summary>
    public record SourceQuote(string Source, decimal Price);

    public static MarketConfidence Compute(MarketMetrics m, IReadOnlyList<SourceQuote>? otherSources = null, string primarySource = "Pokémon TCG API")
    {
        if (m.Market is null)
            return Insufficient("No market reference price is available for this printing.");
        if (m.AgeDays > MaxAgeDays)
            return Insufficient($"The latest price is {m.AgeDays} days old, too old to rate.");
        if (m.Observations30 < MinObservations)
            return Insufficient($"Only {m.Observations30} day{(m.Observations30 == 1 ? "" : "s")} of prices in the last 30; at least {MinObservations} are needed to rate confidence.");

        var factors = new List<ConfidenceFactor>();
        void Add(string name, int penalty, string detail) => factors.Add(new ConfidenceFactor(name, penalty, detail));

        var age = m.AgeDays ?? 0;
        Add("Recency", age switch { <= 1 => 0, <= 3 => 10, <= 7 => 25, _ => 40 },
            age switch { 0 => "Updated today", 1 => "Updated yesterday", _ => $"Last updated {age} days ago" });

        Add("Observations", m.Observations30 switch { >= 20 => 0, >= 10 => 10, _ => 20 },
            $"{m.Observations30} days of prices in the last 30");

        Add("Volatility", m.Volatility switch { VolatilityLevel.Moderate => 10, VolatilityLevel.High => 20, VolatilityLevel.VeryHigh => 30, _ => 0 },
            m.Volatility30 is { } v ? $"{Describe(m.Volatility)} 30-day volatility ({v:0.0}% daily)" : "Not enough daily moves to measure volatility");

        var spread = m.LowVsMarket is { } s ? Math.Abs(s) : (double?)null;
        Add("Listing spread", spread switch { null => 5, <= 15 => 0, <= 35 => 10, _ => 20 },
            m.LowVsMarket is { } gap ? $"Lowest listing {Math.Abs(gap):0}% {(gap >= 0 ? "above" : "below")} the market reference" : "No current lowest listing to compare");

        Add("Range", m.Range30 switch { null or <= 20 => 0, <= 50 => 5, _ => 10 },
            m.Range30 is { } r ? $"30-day high is {r:0}% above the 30-day low" : "No 30-day range yet");

        Add("Outliers", Math.Min(15, m.Outliers30 * 5),
            m.Outliers30 == 0 ? "No outlier price moves in 30 days" : $"{m.Outliers30} outlier price move{(m.Outliers30 == 1 ? "" : "s")} in 30 days");

        Add("History", m.HistoryDays < 30 ? 5 : 0, $"{m.HistoryDays} days of history recorded");

        var others = otherSources ?? [];
        if (others.Count == 0)
        {
            Add("Source agreement", 0, $"Single source ({primarySource} market reference); no verified sales to cross-check");
        }
        else
        {
            var worst = others.Max(o => Math.Abs((double)o.Price / (double)m.Market.Value - 1) * 100);
            Add("Source agreement", worst switch { <= 10 => 0, <= 25 => 10, _ => 20 },
                worst <= 10 ? $"{string.Join(", ", others.Select(o => o.Source))} agree within {worst:0}%" : $"Sources disagree by up to {worst:0}%");
        }

        var score = Math.Clamp(100 - factors.Sum(f => f.Penalty), 0, 100);
        var band = score >= 75 ? ConfidenceBand.High : score >= 50 ? ConfidenceBand.Medium : ConfidenceBand.Low;
        return new MarketConfidence(score, band, Explain(band, m, factors), factors);
    }

    private static MarketConfidence Insufficient(string why) =>
        new(null, ConfidenceBand.InsufficientData, $"Insufficient data — {Lower(why)}", []);

    /// <summary>"{Band} confidence — {what's good} but {the biggest problem}."</summary>
    private static string Explain(ConfidenceBand band, MarketMetrics m, IReadOnlyList<ConfidenceFactor> factors)
    {
        var label = band switch { ConfidenceBand.High => "High", ConfidenceBand.Medium => "Medium", _ => "Low" };
        var good = new List<string>();
        if (factors.Single(f => f.Name == "Recency").Penalty == 0) good.Add("current pricing is recent");
        if (factors.Single(f => f.Name == "Observations").Penalty == 0) good.Add("there's a full month of daily prices");
        if (factors.Single(f => f.Name == "Volatility").Penalty == 0 && m.Volatility != VolatilityLevel.Unknown) good.Add("the price has been steady");

        var worst = factors.Where(f => f.Penalty > 0).OrderByDescending(f => f.Penalty).FirstOrDefault();
        var problem = worst?.Name switch
        {
            null => null,
            "Recency" => $"prices were last updated {m.AgeDays} days ago",
            "Observations" => $"there are only {m.Observations30} days of prices this month",
            "Volatility" => $"this market has shown {Describe(m.Volatility).ToLowerInvariant()} 30-day volatility",
            "Listing spread" => m.LowVsMarket is { } gap
                ? $"the lowest listing is {Math.Abs(gap):0}% {(gap >= 0 ? "above" : "below")} the market reference"
                : "there's no current listing to compare against",
            "Range" => $"the price has ranged {m.Range30:0}% over 30 days",
            "Outliers" => "recent prices include unusual jumps",
            "History" => "price history is still short",
            _ => "the available sources disagree",
        };

        var goodText = good.Count switch { 0 => null, 1 => good[0], _ => $"{string.Join(", ", good.Take(good.Count - 1))} and {good[^1]}" };
        return (goodText, problem) switch
        {
            (not null, not null) => $"{label} confidence — {goodText}, but {problem}.",
            (not null, null) => $"{label} confidence — {goodText}.",
            (null, not null) => $"{label} confidence — {problem}.",
            _ => $"{label} confidence.",
        };
    }

    public static string Describe(VolatilityLevel level) => level switch
    {
        VolatilityLevel.Low => "Low",
        VolatilityLevel.Moderate => "Moderate",
        VolatilityLevel.High => "High",
        VolatilityLevel.VeryHigh => "Very high",
        _ => "Unknown",
    };

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
