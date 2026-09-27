namespace PokemonTCG.API.Market.Intelligence;

/// <summary>A rule-based market signal that fired: what, how big, over what window, and why.</summary>
public record SignalResult(
    string Kind,
    string Name,
    double Value,
    string Unit,
    int LookbackDays,
    string Reason,
    DateOnly AsOf,
    int? Confidence);

/// <summary>
/// Market signals, each a fixed mathematical rule over a printing's daily market reference prices. They describe what
/// the recorded prices did; none is a forecast.
/// </summary>
public static class Signals
{
    public const string Momentum = "Momentum";
    public const string Acceleration = "Acceleration";
    public const string UnusualMove = "UnusualMove";
    public const string VolatilityExpansion = "VolatilityExpansion";
    public const string New30DayHigh = "New30DayHigh";
    public const string New30DayLow = "New30DayLow";
    public const string ThinSupply = "ThinSupply";
    public const string Sleeper = "Sleeper";
    public const string SustainedDowntrend = "SustainedDowntrend";

    /// <summary>The rule behind each signal, for the methodology page and the API.</summary>
    public static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>
    {
        [Momentum] = "Market price up at least 10% over 7 days, and up over 30 days.",
        [Acceleration] = "7-day change at least 8 points above the pace of the 30-day change (30-day change × 7/30), with at least 20 prices in 30 days.",
        [UnusualMove] = "Latest daily move at least 3 standard deviations from the previous 30 daily moves (at least 15 of them).",
        [VolatilityExpansion] = "Standard deviation of the last 7 daily moves at least twice that of the 30 before them.",
        [New30DayHigh] = "Latest market price above every price in the previous 29 days (at least 20 prices).",
        [New30DayLow] = "Latest market price below every price in the previous 29 days (at least 20 prices).",
        [ThinSupply] = "Lowest listing at least 25% above the market reference (and at most 3×, beyond which it's likely bad data).",
        [Sleeper] = "Lowest listing 10% to 200% above the market reference while the market reference moved less than 15% over 30 days.",
        [SustainedDowntrend] = "30-day fit of log price slopes down with r² ≥ 0.6 over at least 5 prices, and the price fell at least 10%.",
    };

    public static IReadOnlyList<SignalResult> Evaluate(PriceSeries series, MarketMetrics m, int? confidence)
    {
        var latest = series.Latest;
        if (latest is null) return [];
        var results = new List<SignalResult>();
        void Add(string kind, string name, double value, string unit, int lookback, string reason) =>
            results.Add(new SignalResult(kind, name, Math.Round(value, 1), unit, lookback, reason, latest.Date, confidence));

        if (m.Change7 is >= 10 && m.Change30 is > 0)
            Add(Momentum, "Momentum", m.Change7.Value, "%", 7, $"Up {m.Change7:0.0}% over 7 days and {m.Change30:0.0}% over 30 days.");

        if (m.Change7 is { } c7 && m.Change30 is { } c30 && m.Observations30 >= 20)
        {
            var pace = c30 * 7 / 30;
            if (c7 > 0 && c7 - pace >= 8)
                Add(Acceleration, "Acceleration", c7 - pace, "pts", 7, $"Up {c7:0.0}% in 7 days against a 30-day pace of {pace:0.0}% a week.");
        }

        var returns = series.Returns(31);
        if (returns.Count >= 16)
        {
            var last = returns[^1];
            var previous = returns.Take(returns.Count - 1).TakeLast(30).ToList();
            if (Stats.StdDev(previous) is { } sd && sd > 0.005 && Math.Abs(last) / sd >= 3)
            {
                var move = (Math.Exp(last) - 1) * 100;
                Add(UnusualMove, "Unusual move", move, "%", 30,
                    $"Moved {move:+0.0;-0.0}% in a day, {Math.Abs(last) / sd:0.0} standard deviations from its usual daily move.");
            }
        }

        var returns37 = series.Returns(37);
        if (returns37.Count >= 20)
        {
            var recent = returns37.TakeLast(7).ToList();
            var before = returns37.Take(returns37.Count - 7).TakeLast(30).ToList();
            if (Stats.StdDev(recent) is { } r && Stats.StdDev(before) is { } b && b > 0.002 && r / b >= 2)
                Add(VolatilityExpansion, "Volatility expansion", r / b, "×", 7, $"Daily moves over the last 7 days are {r / b:0.0}× as large as the 30 days before.");
        }

        var window = series.Window(30);
        if (window.Count >= 20)
        {
            var prior = window.Take(window.Count - 1).Select(p => p.Market!.Value).ToList();
            var now = latest.Market!.Value;
            if (now > prior.Max())
                Add(New30DayHigh, "New 30-day high", SeriesPercent(now, prior.Max()), "%", 30, $"${now:0.00} is above the previous 30-day high of ${prior.Max():0.00}.");
            if (now < prior.Min())
                Add(New30DayLow, "New 30-day low", SeriesPercent(now, prior.Min()), "%", 30, $"${now:0.00} is below the previous 30-day low of ${prior.Min():0.00}.");
        }

        if (m.LowVsMarket is { } gap)
        {
            if (gap >= 25 && gap <= 200)
                Add(ThinSupply, "Thin supply", gap, "%", 1, $"The lowest listing is {gap:0}% above the market reference.");
            var quiet = m.Change30 is null || Math.Abs(m.Change30.Value) < 15;
            if (gap >= 10 && gap <= 200 && quiet)
                Add(Sleeper, "Sleeper", gap, "%", 30,
                    $"The lowest listing is {gap:0}% above the market reference while the 30-day market price is {(m.Change30 is { } c ? $"nearly flat ({c:+0.0;-0.0}%)" : "quiet")}.");
        }

        if (DownTrend(series) is { } drop)
            Add(SustainedDowntrend, "Sustained downtrend", drop.Change, "%", 30,
                $"Down {Math.Abs(drop.Change):0.0}% over 30 days in a steady line (r² {drop.R2:0.00}).");

        return results;
    }

    /// <summary>The Trending Down rule used by the market page: a steady 30-day fall of at least 10%.</summary>
    public static (double Change, double R2)? DownTrend(PriceSeries series)
    {
        var window = series.Window(31);
        if (window.Count < 5) return null;
        if (Stats.LogTrend(window) is not { Slope: < 0 } fit || fit.R2 < 0.6) return null;
        var change = SeriesPercent(window[^1].Market!.Value, window[0].Market!.Value);
        return change <= -10 ? (change, fit.R2) : null;
    }

    private static double SeriesPercent(decimal now, decimal then) => PriceSeries.Percent(now, then);
}

/// <summary>"Why is this moving?": plain sentences built only from measured metrics and fired signals.</summary>
public static class WhyMoving
{
    public static IReadOnlyList<string> Explain(MarketMetrics m, IReadOnlyList<SignalResult> signals)
    {
        var lines = new List<string>();
        if (m.Change7 is { } c7 && Math.Abs(c7) >= 5)
        {
            var dir = c7 > 0 ? "increased" : "decreased";
            lines.Add(m.LowChange7 is { } l7
                ? $"Market price {dir} {Math.Abs(c7):0.0}% over seven days while the current low listing {(l7 >= 0 ? "increased" : "decreased")} {Math.Abs(l7):0.0}%."
                : $"Market price {dir} {Math.Abs(c7):0.0}% over seven days.");
        }
        if (m.Change30 is { } c30 && Math.Abs(c30) >= 10)
            lines.Add($"Over 30 days the market price is {(c30 > 0 ? "up" : "down")} {Math.Abs(c30):0.0}%.");
        if (m.BelowHigh30 is { } below && below >= 5)
            lines.Add($"Price is {below:0}% below its 30-day high.");
        foreach (var signal in signals.Where(s => s.Kind is Signals.Sleeper or Signals.UnusualMove or Signals.SustainedDowntrend or Signals.VolatilityExpansion))
        {
            lines.Add(signal.Kind == Signals.Sleeper
                ? $"Current low is {signal.Value:0}% above market reference while the 30-day market price is nearly flat, triggering the Sleeper rule."
                : $"{signal.Reason} ({signal.Name} rule.)");
        }
        if (lines.Count == 0)
        {
            lines.Add(m.Change7 is null && m.Change30 is null
                ? "There isn't enough price history yet to explain recent movement."
                : "Market price has moved less than 5% over seven days and less than 10% over 30 days.");
        }
        return lines;
    }
}

public enum LiquidityStatus
{
    Unknown,
    Thin,
    Moderate,
    Active,
    VeryActive,
}

/// <summary>
/// Liquidity from verified sales only. Without a sold-comp source the status is Unknown and every metric is null: sales
/// counts are never estimated from reference prices.
/// </summary>
public record Liquidity(
    LiquidityStatus Status,
    string Explanation,
    int? Sales7,
    int? Sales30,
    int? Sales90,
    double? MedianDaysBetweenSales,
    DateTimeOffset? LastSaleAt,
    double? SalesPerDay30,
    /// <summary>Interquartile range of 30-day sale prices as a percent of their median.</summary>
    double? PriceDispersion30)
{
    public static Liquidity Compute(IReadOnlyList<(DateTimeOffset SoldAt, decimal Price)>? sales, DateTimeOffset now, string referenceSource, string? compSource)
    {
        if (sales is null || compSource is null)
            return new Liquidity(LiquidityStatus.Unknown,
                $"Liquidity is unknown: {referenceSource} publishes market reference prices, not individual sales, and no sold-comp source is connected.",
                null, null, null, null, null, null, null);

        var ordered = sales.Where(s => s.SoldAt <= now).OrderBy(s => s.SoldAt).ToList();
        int Count(int days) => ordered.Count(s => s.SoldAt > now.AddDays(-days));
        var s7 = Count(7);
        var s30 = Count(30);
        var s90 = Count(90);
        var recent = ordered.Where(s => s.SoldAt > now.AddDays(-90)).ToList();
        var gaps = recent.Zip(recent.Skip(1), (a, b) => (b.SoldAt - a.SoldAt).TotalDays).ToList();
        var prices30 = ordered.Where(s => s.SoldAt > now.AddDays(-30)).Select(s => (double)s.Price).ToList();
        double? dispersion = null;
        if (prices30.Count >= 4 && Stats.Median(prices30) is { } median && median > 0)
            dispersion = (Stats.Quantile(prices30, 0.75)!.Value - Stats.Quantile(prices30, 0.25)!.Value) / median * 100;

        var status = s30 switch { >= 30 => LiquidityStatus.VeryActive, >= 10 => LiquidityStatus.Active, >= 3 => LiquidityStatus.Moderate, _ => LiquidityStatus.Thin };
        return new Liquidity(status,
            $"{s30} verified sale{(s30 == 1 ? "" : "s")} in 30 days from {compSource}.",
            s7, s30, s90, gaps.Count == 0 ? null : Stats.Median(gaps), ordered.LastOrDefault().SoldAt is var last && last != default ? last : null,
            Math.Round(s30 / 30.0, 2), dispersion is null ? null : Math.Round(dispersion.Value, 1));
    }
}
