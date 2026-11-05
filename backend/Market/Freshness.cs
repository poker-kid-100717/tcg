namespace PokemonTCG.API.Market;

public enum FreshnessState
{
    /// <summary>Updated within <see cref="FreshnessOptions.FreshDays"/>.</summary>
    Fresh,
    /// <summary>Older than fresh but still usable; shown with its age.</summary>
    Aging,
    /// <summary>Older than <see cref="FreshnessOptions.AgingDays"/>: shown as stale, and confidence is capped.</summary>
    Stale,
    NoData,
}

/// <summary>How old a price or signal is, with a label the UI shows as-is.</summary>
public record DataFreshness(DateOnly? AsOf, int? AgeDays, FreshnessState State, string Label);

public class FreshnessOptions
{
    public const string SectionName = "Freshness";
    public int FreshDays { get; set; } = 1;
    public int AgingDays { get; set; } = 3;

    public DataFreshness Of(DateOnly? asOf, DateOnly today)
    {
        if (asOf is null) return new DataFreshness(null, null, FreshnessState.NoData, "No price recorded");
        var age = Math.Max(0, today.DayNumber - asOf.Value.DayNumber);
        var when = age switch { 0 => "today", 1 => "yesterday", _ => $"{age} days ago" };
        return age <= FreshDays
            ? new DataFreshness(asOf, age, FreshnessState.Fresh, $"Updated {when}")
            : age <= AgingDays
                ? new DataFreshness(asOf, age, FreshnessState.Aging, $"Updated {when}")
                : new DataFreshness(asOf, age, FreshnessState.Stale, $"Stale: last updated {when}");
    }
}
