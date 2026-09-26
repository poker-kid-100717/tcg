using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing;
using PokemonTCG.API.Pricing.Predictions;

namespace PokemonTCG.API.Collections;

public class CollectionOptions
{
    public const string SectionName = "Collection";

    /// <summary>TCGplayer seller fees: 10.75% commission, 2.5% + $0.30 payment processing (as of 2026).</summary>
    public decimal CommissionRate { get; set; } = 0.1075m;
    public decimal PaymentRate { get; set; } = 0.025m;
    public decimal PerSaleFee { get; set; } = 0.30m;
}

/// <summary>A request the collector can fix: the message is shown to them.</summary>
public class CollectionException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>
/// A collector's cards and wishlist, valued three ways (market, condition-adjusted, net after TCGplayer fees), with how
/// far each price can be trusted, value over time, set progress, and heads-ups on cards worth a second look.
/// </summary>
public class CollectionService(AppDbContext db, CatalogReader catalog, PokemonTcgClient client, CollectionOptions options, TimeProvider clock)
{
    /// <summary>
    /// Estimated value of a copy in each condition relative to Near Mint, used until condition-specific TCGplayer
    /// prices are available. Played cards typically sell for 50–85% of Near Mint.
    /// </summary>
    public static readonly IReadOnlyDictionary<CardCondition, decimal> ConditionFactors = new Dictionary<CardCondition, decimal>
    {
        [CardCondition.NearMint] = 1m,
        [CardCondition.LightlyPlayed] = 0.85m,
        [CardCondition.ModeratelyPlayed] = 0.7m,
        [CardCondition.HeavilyPlayed] = 0.5m,
        [CardCondition.Damaged] = 0.35m,
    };

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    // ---------------------------------------------------------------- reading

    public async Task<CollectionView> GetAsync(Guid userId, CancellationToken ct)
    {
        var items = await db.CollectionItems.AsNoTracking().Where(i => i.UserId == userId).ToListAsync(ct);
        var context = await LoadContextAsync(items.Select(i => i.CardId), ct);
        var entries = items.Select(i => ToEntry(i, context)).OrderByDescending(e => e.Total ?? 0).ThenBy(e => e.Name).ToList();

        var withValue = entries.Where(e => e.ValueEach is not null).ToList();
        var marketValue = entries.Sum(e => (e.MarketEach ?? 0) * e.Quantity);
        var conditionValue = withValue.Sum(e => e.ValueEach!.Value * e.Quantity);
        var net = withValue.Sum(e => e.NetEach!.Value * e.Quantity);
        var costed = entries.Where(e => e.CostEach is not null).ToList();
        decimal? costBasis = costed.Count == 0 ? null : costed.Sum(e => e.CostEach!.Value * e.Quantity);
        decimal? gain = costBasis is null ? null : costed.Sum(e => (e.ValueEach ?? 0) * e.Quantity) - costBasis;
        var highShare = conditionValue == 0 ? 0 : withValue.Where(e => e.Confidence.Level == "High").Sum(e => e.ValueEach!.Value * e.Quantity) / conditionValue;

        var history = ValueHistory(items, context);
        decimal? change30 = null;
        var thirtyAgo = history.LastOrDefault(p => p.Date <= Today().AddDays(-30));
        if (thirtyAgo is { Value: > 0 } && history.Count > 0) change30 = Math.Round((history[^1].Value / thirtyAgo.Value - 1) * 100, 1);

        var summary = new CollectionSummary(
            entries.Sum(e => e.Quantity), entries.Select(e => e.CardId).Distinct().Count(),
            Math.Round(marketValue, 2), Math.Round(conditionValue, 2), Math.Round(net, 2),
            costBasis is null ? null : Math.Round(costBasis.Value, 2), gain is null ? null : Math.Round(gain.Value, 2),
            costBasis is > 0 ? Math.Round(gain!.Value / costBasis.Value * 100, 1) : null,
            Math.Round(highShare * 100, 0), change30,
            context.Prices.Values.SelectMany(v => v.Values).Select(p => (DateOnly?)p.UpdatedOn).Max());

        return new CollectionView(summary, entries, history, await SetProgressAsync(items, ct), Signals(items, entries, context),
            await GoalsAsync(userId, ct),
            new SellingFees(options.CommissionRate, options.PaymentRate, options.PerSaleFee), ConditionFactors);
    }

    public async Task<CardOwnership> GetCardAsync(Guid userId, string cardId, CancellationToken ct)
    {
        var items = await db.CollectionItems.AsNoTracking().Where(i => i.UserId == userId && i.CardId == cardId).ToListAsync(ct);
        var wish = await db.WishlistItems.AsNoTracking().FirstOrDefaultAsync(w => w.UserId == userId && w.CardId == cardId, ct);
        var context = await LoadContextAsync([cardId], ct);
        return new CardOwnership(
            items.Select(i => ToEntry(i, context)).OrderBy(e => Prices.VariantOrder(e.Variant)).ThenBy(e => e.Condition).ToList(),
            wish is null ? null : ToWish(wish, context));
    }

    /// <summary>Everything the set page needs to show what's owned, what's missing, and what finishing the set would cost.</summary>
    public async Task<SetChecklist?> GetSetAsync(Guid userId, string setId, CancellationToken ct)
    {
        var set = await db.Sets.AsNoTracking().FirstOrDefaultAsync(s => s.Id == setId, ct);
        var cards = await db.Cards.AsNoTracking().Where(c => c.SetId == setId).ToListAsync(ct);
        if (set is null || cards.Count == 0) return null;
        var cardIds = cards.Select(c => c.Id).ToList();
        var owned = await db.CollectionItems.AsNoTracking().Where(i => i.UserId == userId && cardIds.Contains(i.CardId)).ToListAsync(ct);
        var ownedIds = owned.Select(o => o.CardId).ToHashSet();
        var missingCards = cards.Where(c => !ownedIds.Contains(c.Id)).ToList();
        var context = await LoadContextAsync(missingCards.Select(c => c.Id), ct);

        var missing = missingCards
            .OrderBy(c => CatalogReader.NumberSortKey(c.Number)).ThenBy(c => c.Number, StringComparer.Ordinal)
            .Select(c =>
            {
                var cheapest = Cheapest(context.Prices.GetValueOrDefault(c.Id));
                var (timing, reason) = cheapest is null ? (null, null) : Timing(c.Id, cheapest.Variant, cheapest, context);
                return new MissingCard(c.Id, c.Name, c.Number, c.ImageSmall, IsSecret(c.Number, set.PrintedTotal), cheapest?.Variant,
                    cheapest?.Market ?? cheapest?.Mid, cheapest?.Low, timing, reason, CatalogReader.TcgplayerUrl(c));
            }).ToList();

        var allPrices = await PricesByCardAsync(cardIds, ct);
        var master = SetCompletion.Master(set, cards, allPrices, owned.Select(o => (o.CardId, o.Variant)).ToHashSet());
        var goal = await db.SetGoals.AsNoTracking().Where(g => g.UserId == userId && g.SetId == setId).Select(g => (SetGoalKind?)g.Kind).FirstOrDefaultAsync(ct);

        return new SetChecklist(setId, set.PrintedTotal, cards.Count,
            owned.GroupBy(o => o.CardId).Select(g => new OwnedCard(g.Key, g.Sum(o => o.Quantity), g.Select(o => o.Variant).Distinct().ToList())).ToList(),
            missing,
            missing.Where(m => !m.Secret).Sum(m => m.Price ?? 0),
            missing.Sum(m => m.Price ?? 0),
            master,
            goal);
    }

    // ---------------------------------------------------------------- set goals

    /// <summary>Starts (or re-targets) a goal to complete a set. Null when the set isn't in the catalog.</summary>
    public async Task<GoalProgress?> SetGoalAsync(Guid userId, string setId, SetGoalKind kind, CancellationToken ct)
    {
        if (!Enum.IsDefined(kind)) throw new CollectionException(400, "Pick main set, full set or master set.");
        if (!await db.Sets.AnyAsync(s => s.Id == setId, ct)) return null;
        var goal = await db.SetGoals.FirstOrDefaultAsync(g => g.UserId == userId && g.SetId == setId, ct);
        if (goal is null)
        {
            goal = new SetGoal { Id = Guid.CreateVersion7(), UserId = userId, SetId = setId, CreatedAt = clock.GetUtcNow() };
            db.SetGoals.Add(goal);
        }
        goal.Kind = kind;
        await db.SaveChangesAsync(ct);
        return (await GoalsAsync(userId, ct)).Single(g => g.SetId == setId);
    }

    public async Task<bool> RemoveGoalAsync(Guid userId, string setId, CancellationToken ct) =>
        await db.SetGoals.Where(g => g.UserId == userId && g.SetId == setId).ExecuteDeleteAsync(ct) > 0;

    /// <summary>Progress on every set goal, least complete first after the ones closest to done.</summary>
    public async Task<IReadOnlyList<GoalProgress>> GoalsAsync(Guid userId, CancellationToken ct)
    {
        var goals = await db.SetGoals.AsNoTracking().Where(g => g.UserId == userId).ToListAsync(ct);
        if (goals.Count == 0) return [];
        var setIds = goals.Select(g => g.SetId).ToList();
        var sets = await db.Sets.AsNoTracking().Where(s => setIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);
        var cards = (await db.Cards.AsNoTracking().Where(c => setIds.Contains(c.SetId)).ToListAsync(ct)).ToLookup(c => c.SetId);
        var cardIds = cards.SelectMany(g => g).Select(c => c.Id).ToList();
        var prices = await PricesByCardAsync(cardIds, ct);
        var owned = (await db.CollectionItems.AsNoTracking().Where(i => i.UserId == userId && cardIds.Contains(i.CardId))
            .Select(i => new { i.CardId, i.Variant }).ToListAsync(ct)).Select(i => (i.CardId, i.Variant)).ToHashSet();

        return goals.Where(g => sets.ContainsKey(g.SetId))
            .Select(g => SetCompletion.Goal(g.Kind, sets[g.SetId], cards[g.SetId].ToList(), prices, owned))
            .OrderByDescending(g => g.Completion).ThenBy(g => g.SetName)
            .ToList();
    }

    private async Task<Dictionary<string, Dictionary<string, LatestPrice>>> PricesByCardAsync(IReadOnlyCollection<string> cardIds, CancellationToken ct) =>
        (await db.LatestPrices.AsNoTracking().Where(p => cardIds.Contains(p.CardId)).ToListAsync(ct))
            .GroupBy(p => p.CardId).ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Variant));

    public async Task<IReadOnlyList<WishlistEntry>> GetWishlistAsync(Guid userId, CancellationToken ct)
    {
        var wishes = await db.WishlistItems.AsNoTracking().Where(w => w.UserId == userId).ToListAsync(ct);
        var context = await LoadContextAsync(wishes.Select(w => w.CardId), ct);
        return wishes.Select(w => ToWish(w, context)).OrderByDescending(w => w.AtOrBelowTarget).ThenBy(w => w.Name).ToList();
    }

    public async Task<string> ExportCsvAsync(Guid userId, CancellationToken ct)
    {
        var view = await GetAsync(userId, ct);
        var csv = new StringBuilder();
        csv.AppendLine("Card ID,Name,Set,Number,Rarity,Printing,Condition,Quantity,Cost each,Acquired,Market each,Value each (condition),Net each if sold,Total value,Price confidence,Notes");
        foreach (var e in view.Items)
        {
            csv.AppendLine(string.Join(',', new object?[]
            {
                e.CardId, e.Name, e.SetName, e.Number, e.Rarity, e.VariantLabel, ConditionLabel(e.Condition), e.Quantity,
                e.CostEach, e.AcquiredOn?.ToString("yyyy-MM-dd"), e.MarketEach, e.ValueEach, e.NetEach, e.Total, e.Confidence.Level, e.Notes,
            }.Select(Csv)));
        }
        return csv.ToString();
    }

    // ---------------------------------------------------------------- writing

    public async Task<CardOwnership> AddAsync(Guid userId, AddItemRequest request, CancellationToken ct)
    {
        Validate(request.Quantity, request.CostEach, request.Notes);
        var card = await EnsureCardAsync(request.CardId, ct);
        var variants = await db.LatestPrices.Where(p => p.CardId == card.Id).Select(p => p.Variant).ToListAsync(ct);
        if (variants.Count > 0 && !variants.Contains(request.Variant))
            throw new CollectionException(400, $"{card.Name} doesn't come in that printing.");

        var existing = await db.CollectionItems.FirstOrDefaultAsync(
            i => i.UserId == userId && i.CardId == card.Id && i.Variant == request.Variant && i.Condition == request.Condition, ct);
        var now = clock.GetUtcNow();
        if (existing is null)
        {
            db.CollectionItems.Add(new CollectionItem
            {
                Id = Guid.CreateVersion7(), UserId = userId, CardId = card.Id, Variant = request.Variant, Condition = request.Condition,
                Quantity = request.Quantity, CostEach = request.CostEach, AcquiredOn = request.AcquiredOn, Notes = Trim(request.Notes),
                AddedAt = now, UpdatedAt = now,
            });
        }
        else
        {
            existing.CostEach = CombineCost(existing.Quantity, existing.CostEach, request.Quantity, request.CostEach);
            existing.Quantity = checked(existing.Quantity + request.Quantity);
            existing.AcquiredOn ??= request.AcquiredOn;
            existing.Notes = Trim(request.Notes) ?? existing.Notes;
            existing.UpdatedAt = now;
            if (existing.Quantity > 9999) throw new CollectionException(400, "That's more than 9,999 copies.");
        }
        await db.SaveChangesAsync(ct);
        return await GetCardAsync(userId, card.Id, ct);
    }

    public async Task<CardOwnership> UpdateAsync(Guid userId, Guid itemId, UpdateItemRequest request, CancellationToken ct)
    {
        var item = await db.CollectionItems.FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == userId, ct)
            ?? throw new CollectionException(404, "That card isn't in your collection.");
        Validate(request.Quantity ?? item.Quantity, request.CostEach, request.Notes);
        if (request.Quantity is { } quantity) item.Quantity = quantity;
        if (request.ClearCost) item.CostEach = null;
        else if (request.CostEach is { } cost) item.CostEach = cost;
        if (request.AcquiredOn is { } acquired) item.AcquiredOn = acquired;
        if (request.Notes is not null) item.Notes = Trim(request.Notes);
        item.UpdatedAt = clock.GetUtcNow();

        if (request.Condition is { } condition && condition != item.Condition)
        {
            // Changing condition onto a printing already held in that condition merges the two rows.
            var other = await db.CollectionItems.FirstOrDefaultAsync(
                i => i.UserId == userId && i.CardId == item.CardId && i.Variant == item.Variant && i.Condition == condition, ct);
            if (other is null) item.Condition = condition;
            else
            {
                other.CostEach = CombineCost(other.Quantity, other.CostEach, item.Quantity, item.CostEach);
                other.Quantity += item.Quantity;
                other.UpdatedAt = item.UpdatedAt;
                db.CollectionItems.Remove(item);
            }
        }
        await db.SaveChangesAsync(ct);
        return await GetCardAsync(userId, item.CardId, ct);
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid itemId, CancellationToken ct) =>
        await db.CollectionItems.Where(i => i.Id == itemId && i.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    public async Task<WishlistEntry> WishAsync(Guid userId, WishRequest request, CancellationToken ct)
    {
        if (request.TargetPrice is < 0 or > 1_000_000) throw new CollectionException(400, "Enter a target price between $0 and $1,000,000.");
        var card = await EnsureCardAsync(request.CardId, ct);
        var wish = await db.WishlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.CardId == card.Id, ct);
        if (wish is null)
        {
            wish = new WishlistItem { Id = Guid.CreateVersion7(), UserId = userId, CardId = card.Id, AddedAt = clock.GetUtcNow() };
            db.WishlistItems.Add(wish);
        }
        wish.Variant = string.IsNullOrWhiteSpace(request.Variant) ? null : request.Variant;
        wish.TargetPrice = request.TargetPrice;
        await db.SaveChangesAsync(ct);
        var context = await LoadContextAsync([card.Id], ct);
        return ToWish(wish, context);
    }

    public async Task<bool> UnwishAsync(Guid userId, Guid wishId, CancellationToken ct) =>
        await db.WishlistItems.Where(w => w.Id == wishId && w.UserId == userId).ExecuteDeleteAsync(ct) > 0;

    /// <summary>
    /// Fills an empty collection with a realistic sample (valuable cards from recent sets plus a few older ones, in
    /// mixed conditions, with purchase prices from a month ago), so the dashboard can be explored without typing.
    /// </summary>
    public async Task<int> AddSampleAsync(Guid userId, CancellationToken ct)
    {
        if (await db.CollectionItems.AnyAsync(i => i.UserId == userId, ct))
            throw new CollectionException(409, "The sample only goes into an empty collection.");

        var recentSets = await db.Sets.AsNoTracking().OrderByDescending(s => s.ReleaseDate).Select(s => s.Id).Take(12).ToListAsync(ct);
        var candidates = await (
                from p in db.LatestPrices.AsNoTracking()
                join c in db.Cards.AsNoTracking() on p.CardId equals c.Id
                where p.Market >= 1.5m && p.Market <= 400m
                orderby p.Market descending
                select new { c.Id, c.SetId, p.Variant, Market = p.Market!.Value })
            .Take(3000).ToListAsync(ct);
        if (candidates.Count == 0) throw new CollectionException(409, "Prices haven't been recorded yet, so there's nothing to sample.");

        var picks = candidates.Where(c => recentSets.Contains(c.SetId)).GroupBy(c => c.SetId).SelectMany(g => g.DistinctBy(c => c.Id).Take(3)).Take(18)
            .Concat(candidates.Where(c => !recentSets.Contains(c.SetId)).DistinctBy(c => c.Id).Where((_, i) => i % 7 == 0).Take(6))
            .DistinctBy(c => c.Id).ToList();

        var monthAgo = Today().AddDays(-30);
        var ids = picks.Select(p => p.Id).ToList();
        var past = (await db.PriceSnapshots.AsNoTracking().Where(s => ids.Contains(s.CardId) && s.Date <= monthAgo && s.Date > monthAgo.AddDays(-10))
                .ToListAsync(ct))
            .GroupBy(s => (s.CardId, s.Variant)).ToDictionary(g => g.Key, g => g.MaxBy(s => s.Date)!.Market);
        CardCondition[] conditions = [CardCondition.NearMint, CardCondition.NearMint, CardCondition.NearMint, CardCondition.LightlyPlayed, CardCondition.NearMint, CardCondition.ModeratelyPlayed];
        var now = clock.GetUtcNow();
        foreach (var (pick, i) in picks.Select((p, i) => (p, i)))
        {
            var cost = past.GetValueOrDefault((pick.Id, pick.Variant)) ?? Math.Round(pick.Market * (i % 3 == 0 ? 1.12m : 0.86m), 2);
            db.CollectionItems.Add(new CollectionItem
            {
                Id = Guid.CreateVersion7(), UserId = userId, CardId = pick.Id, Variant = pick.Variant, Condition = conditions[i % conditions.Length],
                Quantity = i % 5 == 0 ? 2 : 1, CostEach = cost, AcquiredOn = monthAgo, Notes = "Sample card", AddedAt = now, UpdatedAt = now,
            });
        }
        foreach (var wish in candidates.Where(c => !ids.Contains(c.Id)).DistinctBy(c => c.Id).Take(4))
        {
            db.WishlistItems.Add(new WishlistItem { Id = Guid.CreateVersion7(), UserId = userId, CardId = wish.Id, Variant = wish.Variant, TargetPrice = Math.Round(wish.Market * 0.85m, 2), AddedAt = now });
        }
        await db.SaveChangesAsync(ct);
        return picks.Count;
    }

    // ---------------------------------------------------------------- valuation

    private sealed record Context(
        Dictionary<string, Card> Cards,
        Dictionary<string, Dictionary<string, LatestPrice>> Prices,
        ILookup<(string CardId, string Variant), PriceSnapshot> History,
        Dictionary<(string CardId, string Variant), double> Outlook);

    private async Task<Context> LoadContextAsync(IEnumerable<string> cardIds, CancellationToken ct)
    {
        var ids = cardIds.Distinct().ToList();
        var cards = await db.Cards.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var prices = (await db.LatestPrices.AsNoTracking().Where(p => ids.Contains(p.CardId)).ToListAsync(ct))
            .GroupBy(p => p.CardId).ToDictionary(g => g.Key, g => g.ToDictionary(p => p.Variant));
        var since = Today().AddDays(-120);
        var history = (await db.PriceSnapshots.AsNoTracking().Where(s => ids.Contains(s.CardId) && s.Date >= since).OrderBy(s => s.Date).ToListAsync(ct))
            .ToLookup(s => (s.CardId, s.Variant));

        // The price model's outlook, when its latest run was published.
        var run = await db.PredictionRuns.AsNoTracking()
            .Where(r => r.FinishedAt != null && r.Status != PredictionStatus.Failed && r.Status != PredictionStatus.Running && r.Status != PredictionStatus.Skipped)
            .OrderByDescending(r => r.Id).FirstOrDefaultAsync(ct);
        var outlook = run is { Status: PredictionStatus.Published }
            ? (await db.PricePredictions.AsNoTracking().Where(p => p.RunId == run.Id && ids.Contains(p.CardId)).ToListAsync(ct))
                .ToDictionary(p => (p.CardId, p.Variant), p => p.ChangePercent)
            : [];
        return new Context(cards, prices, history, outlook);
    }

    private CollectionEntry ToEntry(CollectionItem item, Context context)
    {
        var card = context.Cards.GetValueOrDefault(item.CardId);
        var price = context.Prices.GetValueOrDefault(item.CardId)?.GetValueOrDefault(item.Variant);
        var market = price?.Market ?? price?.Mid;
        decimal? value = market is null ? null : Math.Round(market.Value * ConditionFactors[item.Condition], 2);
        decimal? net = value is null ? null : Math.Max(0, Math.Round(value.Value * (1 - options.CommissionRate - options.PaymentRate) - options.PerSaleFee, 2));
        return new CollectionEntry(
            item.Id, item.CardId, card?.Name ?? item.CardId, card?.Number ?? "", card?.SetId ?? "", card?.SetName ?? "", card?.ImageSmall, card?.Rarity,
            item.Variant, Prices.VariantLabel(item.Variant), item.Condition, item.Quantity, item.CostEach, item.AcquiredOn, item.Notes,
            market, value, net, value is null ? null : value * item.Quantity,
            value is not null && item.CostEach is > 0 ? Math.Round((value.Value / item.CostEach.Value - 1) * 100, 1) : null,
            Confidence(price, context.History[(item.CardId, item.Variant)].ToList()),
            card is null ? null : CatalogReader.TcgplayerUrl(card), item.AddedAt);
    }

    /// <summary>
    /// High: the market price moved within the last week, there are 10+ days of prices this month, and the cheapest
    /// listing is within 30% of it. Low: little or no history, a price unchanged for 3+ weeks (few recent sales), or
    /// listings far from the market price. Medium otherwise.
    /// </summary>
    internal PriceConfidence Confidence(LatestPrice? price, IReadOnlyList<PriceSnapshot> history)
    {
        var market = price?.Market ?? price?.Mid;
        if (market is null or 0) return new PriceConfidence("Low", ["No recent TCGplayer price"], null);

        var today = Today();
        var recent = history.Where(h => h.Date >= today.AddDays(-30) && h.Market is not null).ToList();
        int? daysSinceChange = null;
        for (var i = history.Count - 1; i > 0; i--)
        {
            if (history[i].Market != history[i - 1].Market)
            {
                daysSinceChange = today.DayNumber - history[i].Date.DayNumber;
                break;
            }
        }
        if (daysSinceChange is null && history.Count > 0) daysSinceChange = today.DayNumber - history[0].Date.DayNumber;
        var gap = price!.Low is { } low ? (double)(low / market.Value - 1) : (double?)null;

        var reasons = new List<string>();
        var low_ = false;
        if (recent.Count < 3) { reasons.Add(history.Count == 0 ? "No daily price history yet (cards under $0.50 aren't tracked daily)" : "Only a few days of prices this month"); low_ = true; }
        if (daysSinceChange > 21) { reasons.Add($"Market price unchanged for {daysSinceChange} days, so few recent sales"); low_ = true; }
        if (gap > 1) { reasons.Add("Cheapest listing is more than double the market price"); low_ = true; }
        if (low_) return new PriceConfidence("Low", reasons, daysSinceChange);

        if (recent.Count >= 10 && daysSinceChange <= 7 && Math.Abs(gap ?? 0) <= 0.3)
            return new PriceConfidence("High", [$"Price moved {Days(daysSinceChange)}", $"{recent.Count} days of prices this month", "Listings close to the market price"], daysSinceChange);

        if (daysSinceChange > 7) reasons.Add($"Last price change {Days(daysSinceChange)}");
        if (recent.Count < 10) reasons.Add($"{recent.Count} days of prices this month");
        if (Math.Abs(gap ?? 0) > 0.3) reasons.Add($"Cheapest listing {(gap > 0 ? "above" : "below")} the market price by {Math.Abs(gap!.Value) * 100:0}%");
        return new PriceConfidence("Medium", reasons, daysSinceChange);
    }

    private static string Days(int? days) => days switch { 0 => "today", 1 => "yesterday", _ => $"{days} days ago" };

    /// <summary>What the collection as it stands today was worth on each of the last 90 days.</summary>
    private List<ValuePoint> ValueHistory(IReadOnlyList<CollectionItem> items, Context context)
    {
        if (items.Count == 0) return [];
        var today = Today();
        var start = today.AddDays(-89);
        var series = items.Select(item =>
        {
            var points = context.History[(item.CardId, item.Variant)].Where(h => h.Market is not null).ToList();
            var latest = context.Prices.GetValueOrDefault(item.CardId)?.GetValueOrDefault(item.Variant);
            return (Item: item, Points: points, Fallback: latest?.Market ?? latest?.Mid ?? 0m);
        }).ToList();
        var firstData = series.SelectMany(s => s.Points).Select(p => (DateOnly?)p.Date).Min();
        if (firstData is null) return [new ValuePoint(today, Math.Round(series.Sum(s => s.Fallback * s.Item.Quantity * ConditionFactors[s.Item.Condition]), 2))];
        if (firstData > start) start = firstData.Value;

        var result = new List<ValuePoint>();
        for (var day = start; day <= today; day = day.AddDays(1))
        {
            decimal total = 0;
            foreach (var (item, points, fallback) in series)
            {
                // The latest price on or before the day; before a card's first recorded price, its first one.
                var price = points.LastOrDefault(p => p.Date <= day)?.Market ?? points.FirstOrDefault()?.Market ?? fallback;
                total += price * item.Quantity * ConditionFactors[item.Condition];
            }
            result.Add(new ValuePoint(day, Math.Round(total, 2)));
        }
        return result;
    }

    private async Task<List<SetProgress>> SetProgressAsync(IReadOnlyList<CollectionItem> items, CancellationToken ct)
    {
        var ownedByCard = items.Select(i => i.CardId).ToHashSet();
        var ownedCards = await db.Cards.AsNoTracking().Where(c => ownedByCard.Contains(c.Id)).Select(c => new { c.Id, c.SetId }).ToListAsync(ct);
        var setIds = ownedCards.Select(c => c.SetId).Distinct().ToList();
        var sets = await db.Sets.AsNoTracking().Where(s => setIds.Contains(s.Id)).ToListAsync(ct);
        var setCards = await db.Cards.AsNoTracking().Where(c => setIds.Contains(c.SetId)).Select(c => new { c.Id, c.SetId, c.Number }).ToListAsync(ct);
        var missingIds = setCards.Where(c => !ownedByCard.Contains(c.Id)).Select(c => c.Id).ToList();
        var prices = await catalog.PricesForAsync(missingIds, ct);

        return sets.Select(set =>
        {
            var cards = setCards.Where(c => c.SetId == set.Id).ToList();
            var baseCards = cards.Where(c => !IsSecret(c.Number, set.PrintedTotal)).ToList();
            var ownedBase = baseCards.Count(c => ownedByCard.Contains(c.Id));
            var missingBase = baseCards.Where(c => !ownedByCard.Contains(c.Id)).ToList();
            var missingPrices = missingBase.Select(c => Prices.HeadlinePrice(prices.GetValueOrDefault(c.Id))).ToList();
            var printed = set.PrintedTotal > 0 ? set.PrintedTotal : baseCards.Count;
            return new SetProgress(set.Id, set.Name, set.SymbolUrl, set.ReleaseDate, ownedBase, printed,
                cards.Count(c => ownedByCard.Contains(c.Id)), Math.Max(set.Total, cards.Count),
                printed == 0 ? 0 : Math.Round(Math.Min(100m, ownedBase * 100m / printed), 1),
                missingPrices.Any(p => p is not null) ? missingPrices.Sum(p => p ?? 0) : null,
                missingPrices.Count(p => p is null));
        }).OrderByDescending(s => s.Completion).ThenBy(s => s.SetName).ToList();
    }

    /// <summary>Heads-ups on owned cards, strongest first. Signals, not financial advice.</summary>
    private static List<CollectionSignal> Signals(IReadOnlyList<CollectionItem> items, IReadOnlyList<CollectionEntry> entries, Context context)
    {
        var signals = new List<(CollectionSignal Signal, decimal Weight)>();
        foreach (var entry in entries.Where(e => e.ValueEach is >= 2))
        {
            var history = context.History[(entry.CardId, entry.Variant)].Where(h => h.Market is not null).ToList();
            var trend = DownTrend(history);
            var outlook = context.Outlook.TryGetValue((entry.CardId, entry.Variant), out var o) ? o : (double?)null;
            var price = context.Prices.GetValueOrDefault(entry.CardId)?.GetValueOrDefault(entry.Variant);
            var sleeper = price is { Market: > 0, Low: { } low } && low >= price.Market.Value * 1.1m && low <= price.Market.Value * 3
                          && Change(history, 30) is { } c30 && Math.Abs(c30) <= 15;

            var reasons = new List<string>();
            string? kind = null;
            if (entry.GainPercent >= 25 && (trend is not null || outlook <= -5))
            {
                kind = "ConsiderSelling";
                reasons.Add($"Up {entry.GainPercent:0}% on what you paid");
            }
            else if (trend is not null || outlook <= -8) kind = "Watch";
            else if (sleeper || outlook >= 8) kind = "Hold";
            if (kind is null) continue;

            if (trend is not null) reasons.Add($"Down {Math.Abs(trend.Value):0}% in a steady slide over 30 days");
            if (outlook is { } change && Math.Abs(change) >= 5) reasons.Add($"Price model expects {(change > 0 ? "+" : "")}{change:0}% over 30 days");
            if (sleeper) reasons.Add("Nothing listed near its market price: supply is thin");
            signals.Add((new CollectionSignal(entry.CardId, entry.Name, entry.SetName, entry.ImageUrl, entry.Variant, entry.VariantLabel, kind, reasons, entry.ValueEach, entry.Quantity),
                (entry.Total ?? 0) * (kind == "ConsiderSelling" ? 3 : kind == "Watch" ? 2 : 1)));
        }
        return signals.OrderByDescending(s => s.Weight).Select(s => s.Signal).Take(8).ToList();
    }

    /// <summary>The % drop when the last 30 days of prices fall steadily (least-squares slope down, r² ≥ 0.6, 10%+ drop), else null.</summary>
    internal static double? DownTrend(IReadOnlyList<PriceSnapshot> history)
    {
        if (history.Count == 0) return null;
        var last = history[^1].Date;
        var points = history.Where(h => h.Date >= last.AddDays(-30) && h.Market > 0).ToList();
        if (points.Count < 5) return null;
        var xs = points.Select(p => (double)(p.Date.DayNumber - last.DayNumber)).ToArray();
        var ys = points.Select(p => Math.Log((double)p.Market!.Value)).ToArray();
        double mx = xs.Average(), my = ys.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (var i = 0; i < xs.Length; i++) { sxy += (xs[i] - mx) * (ys[i] - my); sxx += (xs[i] - mx) * (xs[i] - mx); syy += (ys[i] - my) * (ys[i] - my); }
        if (sxx == 0 || syy == 0) return null;
        var slope = sxy / sxx;
        var r2 = sxy * sxy / (sxx * syy);
        var change = ((double)points[^1].Market!.Value / (double)points[0].Market!.Value - 1) * 100;
        return slope < 0 && r2 >= 0.6 && change <= -10 ? change : null;
    }

    private static double? Change(IReadOnlyList<PriceSnapshot> history, int days)
    {
        if (history.Count == 0) return null;
        var last = history[^1];
        var then = history.LastOrDefault(h => h.Date <= last.Date.AddDays(-days) && h.Date > last.Date.AddDays(-days - 10));
        return then?.Market is > 0 && last.Market is not null ? ((double)(last.Market.Value / then.Market.Value) - 1) * 100 : null;
    }

    /// <summary>For a card on someone's to-buy list: wait when it's sliding or forecast to drop; go when it's at a 90-day low.</summary>
    private (string?, string?) Timing(string cardId, string variant, LatestPrice price, Context context)
    {
        var history = context.History[(cardId, variant)].Where(h => h.Market is not null).ToList();
        if (DownTrend(history) is { } drop) return ("Wait", $"Falling steadily ({drop:0}% in 30 days)");
        if (context.Outlook.TryGetValue((cardId, variant), out var o) && o <= -5) return ("Wait", $"Price model expects {o:0}% over 30 days");
        var ninety = history.Where(h => h.Date >= Today().AddDays(-90)).Select(h => h.Market!.Value).Order().ToList();
        if (ninety.Count >= 20 && (price.Market ?? price.Mid) is { } now && now <= ninety[(int)(ninety.Count * 0.2)])
            return ("Good time", "Near its lowest price in 90 days");
        return (null, null);
    }

    private WishlistEntry ToWish(WishlistItem wish, Context context)
    {
        var card = context.Cards.GetValueOrDefault(wish.CardId);
        var prices = context.Prices.GetValueOrDefault(wish.CardId);
        var price = wish.Variant is not null ? prices?.GetValueOrDefault(wish.Variant) : Cheapest(prices);
        var market = price?.Market ?? price?.Mid;
        decimal? suggested = null;
        if (price is not null)
        {
            var ninety = context.History[(wish.CardId, price.Variant)].Where(h => h.Date >= Today().AddDays(-90) && h.Market is not null).Select(h => h.Market!.Value).Order().ToList();
            suggested = ninety.Count >= 10 ? ninety[(int)(ninety.Count * 0.2)] : market is null ? null : Math.Round(market.Value * 0.9m, 2);
        }
        var buyAt = price?.Low ?? market;
        return new WishlistEntry(wish.Id, wish.CardId, card?.Name ?? wish.CardId, card?.Number ?? "", card?.SetId ?? "", card?.SetName ?? "", card?.ImageSmall,
            wish.Variant ?? price?.Variant, Prices.VariantLabel(wish.Variant ?? price?.Variant ?? "normal"), wish.TargetPrice, suggested, market, price?.Low,
            wish.TargetPrice is not null && buyAt is not null && buyAt <= wish.TargetPrice, card is null ? null : CatalogReader.TcgplayerUrl(card), wish.AddedAt);
    }

    private static LatestPrice? Cheapest(IReadOnlyDictionary<string, LatestPrice>? prices) =>
        prices?.Values.Where(p => (p.Market ?? p.Mid) is not null).MinBy(p => p.Market ?? p.Mid);

    // ---------------------------------------------------------------- helpers

    /// <summary>Makes sure a card is in the local catalog (collections reference it), fetching it once if it isn't.</summary>
    private async Task<Card> EnsureCardAsync(string cardId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cardId) || cardId.Length > 64) throw new CollectionException(400, "Pick a card.");
        if (await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId, ct) is { } known) return known;
        var remote = await client.GetCardAsync(cardId, ct) ?? throw new CollectionException(404, "That card doesn't exist.");
        var card = new Card
        {
            Id = remote.Id, Name = remote.Name, Number = remote.Number, SetId = remote.Set.Id, SetName = remote.Set.Name, Rarity = remote.Rarity,
            ImageSmall = remote.Images?.Small, ImageLarge = remote.Images?.Large, TcgplayerUrl = remote.Tcgplayer?.Url, Supertype = remote.Supertype,
            Subtypes = remote.Subtypes?.ToArray() ?? [], Types = remote.Types?.ToArray() ?? [], Hp = remote.Hp, Artist = remote.Artist,
            FlavorText = remote.FlavorText, NationalDex = remote.NationalPokedexNumbers is [var dex, ..] ? dex : null,
            SetSeries = remote.Set.Series, SetReleased = remote.Set.Released, SetPrintedTotal = remote.Set.PrintedTotal,
        };
        db.Cards.Add(card);
        foreach (var (variant, price) in remote.Tcgplayer?.Prices ?? new Dictionary<string, TcgPrice>())
        {
            db.LatestPrices.Add(new LatestPrice { CardId = card.Id, Variant = variant, Market = price.Market, Low = price.Low, Mid = price.Mid, High = price.High, UpdatedOn = remote.Tcgplayer?.Updated ?? Today() });
        }
        await db.SaveChangesAsync(ct);
        return card;
    }

    public static decimal? CombineCost(int quantityA, decimal? costA, int quantityB, decimal? costB) => (costA, costB) switch
    {
        ({ } a, { } b) => Math.Round((a * quantityA + b * quantityB) / (quantityA + quantityB), 2),
        (null, { } b) => b,
        ({ } a, null) => a,
        _ => null,
    };

    internal static bool IsSecret(string number, int printedTotal) =>
        printedTotal > 0 && number.All(char.IsDigit) && int.TryParse(number, out var n) && n > printedTotal;

    private static void Validate(int quantity, decimal? cost, string? notes)
    {
        if (quantity is < 1 or > 9999) throw new CollectionException(400, "Enter a quantity from 1 to 9,999.");
        if (cost is < 0 or > 1_000_000) throw new CollectionException(400, "Enter a price paid between $0 and $1,000,000.");
        if (notes?.Length > 500) throw new CollectionException(400, "Keep notes under 500 characters.");
    }

    private static string? Trim(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();

    public static string ConditionLabel(CardCondition condition) => condition switch
    {
        CardCondition.NearMint => "Near Mint",
        CardCondition.LightlyPlayed => "Lightly Played",
        CardCondition.ModeratelyPlayed => "Moderately Played",
        CardCondition.HeavilyPlayed => "Heavily Played",
        _ => "Damaged",
    };

    private static string Csv(object? value)
    {
        var text = value switch
        {
            null => "",
            decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
        // Quote everything; defuse values a spreadsheet would run as a formula.
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]) && value is not decimal and not int) text = "'" + text;
        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
