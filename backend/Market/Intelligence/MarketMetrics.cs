namespace PokemonTCG.API.Market.Intelligence;

/// <summary>How much a printing's price moves day to day, from the 30-day standard deviation of daily returns.</summary>
public enum VolatilityLevel
{
    Unknown,
    Low,
    Moderate,
    High,
    VeryHigh,
}

/// <summary>Everything the models read, measured once from a printing's reference-price series.</summary>
public record MarketMetrics(
    decimal? Market,
    decimal? Low,
    decimal? Mid,
    decimal? High,
    DateOnly? AsOf,
    int? AgeDays,
    int Observations30,
    int HistoryDays,
    double? Change1,
    double? Change7,
    double? Change30,
    double? Change90,
    double? LowChange7,
    /// <summary>Standard deviation of daily log returns over 30 days, in percent.</summary>
    double? Volatility30,
    VolatilityLevel Volatility,
    decimal? High30,
    decimal? Low30,
    /// <summary>How far the market price sits below its 30-day high, in percent (0 at the high).</summary>
    double? BelowHigh30,
    /// <summary>The 30-day high/low range as a percent of the low.</summary>
    double? Range30,
    /// <summary>Current lowest listing against the market reference, in percent (positive: listings above market).</summary>
    double? LowVsMarket,
    /// <summary>Daily moves in the last 30 days far outside the printing's usual moves.</summary>
    int Outliers30)
{
    public static MarketMetrics From(PriceSeries series, DateOnly today)
    {
        var latest = series.Latest;
        var window30 = series.Window(30);
        var returns30 = series.Returns(30);
        var vol = Stats.StdDev(returns30) is { } sd && returns30.Count >= 5 ? sd * 100 : (double?)null;
        var high30 = window30.Count == 0 ? (decimal?)null : window30.Max(p => p.Market!.Value);
        var low30 = window30.Count == 0 ? (decimal?)null : window30.Min(p => p.Market!.Value);
        return new MarketMetrics(
            latest?.Market, latest?.Low, latest?.Mid, latest?.High, latest?.Date,
            latest is null ? null : Math.Max(0, today.DayNumber - latest.Date.DayNumber),
            window30.Count,
            series.Points.Count == 0 ? 0 : series.Points[^1].Date.DayNumber - series.Points[0].Date.DayNumber + 1,
            series.Change(1), series.Change(7), series.Change(30), series.Change(90), series.LowChange(7),
            vol, Level(vol),
            high30, low30,
            latest?.Market is { } m && high30 is { } h && h > 0 ? Math.Max(0, (1 - (double)m / (double)h) * 100) : null,
            high30 is { } hi && low30 is { } lo && lo > 0 ? ((double)hi / (double)lo - 1) * 100 : null,
            latest is { Market: { } mk, Low: { } l } && mk > 0 ? ((double)l / (double)mk - 1) * 100 : null,
            CountOutliers(returns30));
    }

    public static VolatilityLevel Level(double? volatility) => volatility switch
    {
        null => VolatilityLevel.Unknown,
        < 2 => VolatilityLevel.Low,
        < 5 => VolatilityLevel.Moderate,
        < 10 => VolatilityLevel.High,
        _ => VolatilityLevel.VeryHigh,
    };

    /// <summary>
    /// Daily returns more than 5 median absolute deviations from the median return, or any single-day move over 50%.
    /// A robust rule: one bad price can't hide itself by inflating the standard deviation.
    /// </summary>
    public static int CountOutliers(IReadOnlyList<double> returns)
    {
        if (returns.Count < 5) return 0;
        var median = Stats.Median(returns)!.Value;
        var mad = Stats.Median(returns.Select(r => Math.Abs(r - median)))!.Value;
        return returns.Count(r => Math.Abs(r) > Math.Log(1.5) || (mad > 0.001 && Math.Abs(r - median) > 5 * mad));
    }
}
