namespace PokemonTCG.API.Market.Intelligence;

/// <summary>One day's market reference prices for a printing (a provider's computed prices, not sales).</summary>
public record PricePointDay(DateOnly Date, decimal? Market, decimal? Low = null, decimal? Mid = null, decimal? High = null);

/// <summary>A printing's daily market reference prices, oldest first, with lookups "as of" a day.</summary>
public sealed class PriceSeries
{
    public PriceSeries(IEnumerable<PricePointDay> points) =>
        Points = points.Where(p => p.Market is > 0).OrderBy(p => p.Date).ToList();

    public IReadOnlyList<PricePointDay> Points { get; }
    public PricePointDay? Latest => Points.Count == 0 ? null : Points[^1];

    /// <summary>The latest point on or before <paramref name="day"/>, looking back at most <paramref name="tolerance"/> days.</summary>
    public PricePointDay? AsOf(DateOnly day, int tolerance = 7)
    {
        for (var i = Points.Count - 1; i >= 0; i--)
        {
            if (Points[i].Date > day) continue;
            return day.DayNumber - Points[i].Date.DayNumber <= tolerance ? Points[i] : null;
        }
        return null;
    }

    /// <summary>Points in the <paramref name="days"/> days up to and including the latest point.</summary>
    public IReadOnlyList<PricePointDay> Window(int days) =>
        Latest is null ? [] : Points.Where(p => p.Date > Latest.Date.AddDays(-days)).ToList();

    /// <summary>% change of the market price over <paramref name="days"/>, or null without a price that far back.</summary>
    public double? Change(int days)
    {
        if (Latest is null) return null;
        var then = AsOf(Latest.Date.AddDays(-days), Math.Max(2, days / 4));
        return then is null ? null : Percent(Latest.Market!.Value, then.Market!.Value);
    }

    /// <summary>% change of the lowest listing over <paramref name="days"/>, when both ends have one.</summary>
    public double? LowChange(int days)
    {
        if (Latest?.Low is not { } now) return null;
        var then = AsOf(Latest.Date.AddDays(-days), Math.Max(2, days / 4));
        return then?.Low is { } before && before > 0 ? Percent(now, before) : null;
    }

    /// <summary>Daily log returns between consecutive recorded days in the last <paramref name="days"/> days.</summary>
    public IReadOnlyList<double> Returns(int days)
    {
        var window = Window(days + 1);
        var returns = new List<double>();
        for (var i = 1; i < window.Count; i++)
        {
            returns.Add(Math.Log((double)window[i].Market!.Value / (double)window[i - 1].Market!.Value));
        }
        return returns;
    }

    public static double Percent(decimal now, decimal then) => ((double)now / (double)then - 1) * 100;
}

/// <summary>Small statistics helpers, kept here so every model uses the same definitions.</summary>
public static class Stats
{
    public static double? StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return null;
        var mean = values.Average();
        return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
    }

    public static double? Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>The value at quantile <paramref name="q"/> (0–1), by linear interpolation.</summary>
    public static double? Quantile(IEnumerable<double> values, double q)
    {
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0) return null;
        var pos = (sorted.Length - 1) * q;
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
    }

    /// <summary>Least-squares fit of ln(price) on day number: slope per day and r².</summary>
    public static (double Slope, double R2)? LogTrend(IReadOnlyList<PricePointDay> points)
    {
        if (points.Count < 2) return null;
        var xs = points.Select(p => (double)p.Date.DayNumber).ToArray();
        var ys = points.Select(p => Math.Log((double)p.Market!.Value)).ToArray();
        double mx = xs.Average(), my = ys.Average(), sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < xs.Length; i++)
        {
            sxy += (xs[i] - mx) * (ys[i] - my);
            sxx += (xs[i] - mx) * (xs[i] - mx);
            syy += (ys[i] - my) * (ys[i] - my);
        }
        if (sxx == 0) return null;
        return (sxy / sxx, syy == 0 ? 0 : sxy * sxy / (sxx * syy));
    }
}
