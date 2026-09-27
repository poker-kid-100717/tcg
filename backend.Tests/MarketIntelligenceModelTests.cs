using PokemonTCG.API.Market.Intelligence;

namespace PokemonTcgMarketplace.Backend.Tests
{
    public class MarketIntelligenceModelTests
    {
        private static readonly DateOnly Today = new(2026, 9, 27);

        /// <summary>A daily series ending <paramref name="endAgo"/> days before today, one price per day.</summary>
        private static PriceSeries Series(Func<int, decimal> marketDaysAgo, int days = 60, int endAgo = 0, Func<int, decimal?>? lowDaysAgo = null) =>
            new(Enumerable.Range(endAgo, days).Select(ago =>
                new PricePointDay(Today.AddDays(-ago), marketDaysAgo(ago), lowDaysAgo?.Invoke(ago) ?? marketDaysAgo(ago) * 0.95m)));

        private static (MarketMetrics Metrics, MarketConfidence Confidence) Rate(PriceSeries series)
        {
            var metrics = MarketMetrics.From(series, Today);
            return (metrics, MarketConfidence.Compute(metrics));
        }

        [Fact]
        public void A_recent_steady_well_observed_price_is_high_confidence()
        {
            var (_, confidence) = Rate(Series(ago => 100m + (ago % 2) * 0.5m));
            Assert.Equal(ConfidenceBand.High, confidence.Band);
            Assert.InRange(confidence.Score!.Value, 75, 100);
            Assert.StartsWith("High confidence — current pricing is recent", confidence.Explanation);
            Assert.Contains(confidence.Factors, f => f.Name == "Source agreement" && f.Detail.Contains("no verified sales"));
        }

        [Fact]
        public void A_volatile_market_says_so_in_one_sentence()
        {
            // ±6% swings every day.
            var (metrics, confidence) = Rate(Series(ago => ago % 2 == 0 ? 100m : 112m));
            Assert.Equal(VolatilityLevel.VeryHigh, metrics.Volatility);
            Assert.Equal(ConfidenceBand.Medium, confidence.Band);
            Assert.Equal("Medium confidence — current pricing is recent and there's a full month of daily prices, but this market has shown very high 30-day volatility.",
                confidence.Explanation);
        }

        [Fact]
        public void Too_few_or_too_old_prices_are_insufficient_data_not_a_low_score()
        {
            var few = Rate(new PriceSeries([new(Today, 10m), new(Today.AddDays(-1), 10m)])).Confidence;
            Assert.Equal((ConfidenceBand.InsufficientData, (int?)null), (few.Band, few.Score));
            Assert.Contains("only 2 days of prices", few.Explanation);

            var stale = Rate(Series(_ => 10m, endAgo: 20)).Confidence;
            Assert.Equal(ConfidenceBand.InsufficientData, stale.Band);
            Assert.Contains("20 days old", stale.Explanation);

            var none = MarketConfidence.Compute(MarketMetrics.From(new PriceSeries([]), Today));
            Assert.Equal(ConfidenceBand.InsufficientData, none.Band);
        }

        [Fact]
        public void Disagreeing_sources_cost_confidence_and_agreeing_ones_do_not()
        {
            var metrics = MarketMetrics.From(Series(_ => 100m), Today);
            var agree = MarketConfidence.Compute(metrics, [new MarketConfidence.SourceQuote("Verified sales", 104m)]);
            var disagree = MarketConfidence.Compute(metrics, [new MarketConfidence.SourceQuote("Verified sales", 140m)]);
            Assert.True(agree.Score > disagree.Score);
            Assert.Contains(disagree.Factors, f => f.Name == "Source agreement" && f.Penalty == 20);
        }

        [Fact]
        public void An_outlier_day_is_counted()
        {
            var (metrics, _) = Rate(Series(ago => ago == 3 ? 300m : 100m));
            Assert.True(metrics.Outliers30 >= 1);
        }

        [Fact]
        public void Momentum_new_high_and_why_moving_come_from_the_numbers()
        {
            // Flat at 100 until a week ago, then a climb to 120; the low listing rises faster.
            var series = Series(ago => ago >= 7 ? 100m : 120m - ago * 2m, lowDaysAgo: ago => ago >= 7 ? 95m : 130m - ago * 2m);
            var metrics = MarketMetrics.From(series, Today);
            var signals = Signals.Evaluate(series, metrics, 80);
            Assert.Contains(signals, s => s.Kind == Signals.Momentum && s.Value == 20.0 && s.LookbackDays == 7);
            Assert.Contains(signals, s => s.Kind == Signals.New30DayHigh);
            Assert.DoesNotContain(signals, s => s.Kind == Signals.SustainedDowntrend);
            var why = WhyMoving.Explain(metrics, signals);
            Assert.Contains("Market price increased 20.0% over seven days while the current low listing increased 36.8%.", why);
        }

        [Fact]
        public void A_sleeper_matches_the_market_page_rule()
        {
            var series = Series(_ => 50m, lowDaysAgo: _ => 59m); // listing 18% above a flat market
            var metrics = MarketMetrics.From(series, Today);
            var signals = Signals.Evaluate(series, metrics, 70);
            var sleeper = Assert.Single(signals, s => s.Kind == Signals.Sleeper);
            Assert.Equal(18.0, sleeper.Value);
            Assert.DoesNotContain(signals, s => s.Kind == Signals.ThinSupply); // under the 25% thin-supply bar
            Assert.Contains("Current low is 18% above market reference while the 30-day market price is nearly flat, triggering the Sleeper rule.",
                WhyMoving.Explain(metrics, signals));
        }

        [Fact]
        public void A_steady_slide_is_a_sustained_downtrend_and_one_bad_day_is_not()
        {
            var slide = Series(ago => 40m - (30 - Math.Min(ago, 30)) * 0.4m);
            Assert.Contains(Signals.Evaluate(slide, MarketMetrics.From(slide, Today), 60), s => s.Kind == Signals.SustainedDowntrend);

            var blip = Series(ago => ago == 0 ? 30m : 40m);
            var metrics = MarketMetrics.From(blip, Today);
            var signals = Signals.Evaluate(blip, metrics, 60);
            Assert.DoesNotContain(signals, s => s.Kind == Signals.SustainedDowntrend);
            Assert.Contains(signals, s => s.Kind == Signals.New30DayLow);
        }

        [Fact]
        public void An_unusual_move_needs_a_quiet_history_to_stand_out_from()
        {
            var jump = Series(ago => ago == 0 ? 130m : 100m + (ago % 2));
            var signal = Assert.Single(Signals.Evaluate(jump, MarketMetrics.From(jump, Today), 60), s => s.Kind == Signals.UnusualMove);
            Assert.True(signal.Value > 25);
        }

        [Fact]
        public void Without_sold_comps_liquidity_is_unknown_and_nothing_is_estimated()
        {
            var liquidity = Liquidity.Compute(null, DateTimeOffset.UtcNow, "Pokémon TCG API", null);
            Assert.Equal(LiquidityStatus.Unknown, liquidity.Status);
            Assert.Null(liquidity.Sales30);
            Assert.Null(liquidity.MedianDaysBetweenSales);
            Assert.Contains("publishes market reference prices, not individual sales", liquidity.Explanation);
        }

        [Fact]
        public void With_verified_sales_liquidity_is_measured()
        {
            var now = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
            var sales = Enumerable.Range(0, 12).Select(i => (now.AddDays(-i * 2.5), 100m + i)).ToList();
            var liquidity = Liquidity.Compute(sales, now, "Pokémon TCG API", "Licensed comps");
            Assert.Equal(LiquidityStatus.Active, liquidity.Status);
            Assert.Equal((3, 12, 12), (liquidity.Sales7!.Value, liquidity.Sales30!.Value, liquidity.Sales90!.Value));
            Assert.Equal(2.5, liquidity.MedianDaysBetweenSales!.Value, 3);
            Assert.Equal(now, liquidity.LastSaleAt);
        }
    }
}
