using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using PokemonTCG.API.Data;
using PokemonTCG.API.Pricing.Predictions;

namespace PokemonTcgMarketplace.Backend.Tests
{
    [Collection(PredictionsCollection.Name)]
    public class PredictionTests(PredictionsFixture fixture)
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
        private const int Days = 60;

        private async Task<T> Get<T>(string path)
        {
            var response = await fixture.CreateClient().GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<T>(Json))!;
        }

        private async Task<PredictionRunResult> Run()
        {
            var response = await fixture.CreateClient().PostAsync("/internal/predictions", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<PredictionRunResult>(Json))!;
        }

        /// <summary>Small deterministic noise, so no two cards move identically.</summary>
        private static double Noise(int card, int day) => Math.Sin(card * 12.9898 + day * 78.233) * 0.01;

        /// <summary>
        /// Sixty days of prices for 60 cards: Pikachu (#25) cards climb 1% a day, Bulbasaur (#1) cards
        /// slide 0.8% a day, and everything else drifts around flat. A model that learns the Pokémon or
        /// the momentum can beat "no change"; one that can't won't.
        /// </summary>
        private async Task SeedAsync()
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (int Dex, string Name, double Daily)[] pokemon = [(25, "Pikachu", 0.01), (1, "Bulbasaur", -0.008), (4, "Charmander", 0), (7, "Squirtle", 0)];
            string[] rarities = ["Rare Holo", "Illustration Rare", "Common", "Double Rare"];

            for (var i = 0; i < 60; i++)
            {
                var (dex, name, daily) = pokemon[i % pokemon.Length];
                var set = $"p{i % 3}";
                var id = $"{set}-{i}";
                db.Cards.Add(new Card
                {
                    Id = id, Name = i % 8 < 4 ? name : $"{name} ex", Number = (i + 1).ToString(), SetId = set, SetName = $"Prediction Set {i % 3}",
                    Rarity = rarities[i / 4 % rarities.Length], Supertype = "Pokémon", Subtypes = i % 8 < 4 ? ["Basic"] : ["Basic", "ex"],
                    NationalDex = dex, Artist = $"Artist {i % 5}", SetSeries = "Test", SetReleased = Today.AddDays(-400 + 100 * (i % 3)),
                    SetPrintedTotal = 100,
                });
                var start = 5.0 + i % 7 * 3;
                for (var day = 0; day <= Days; day++)
                {
                    var market = (decimal)Math.Round(start * Math.Exp(daily * day + Noise(i, day)), 2);
                    db.PriceSnapshots.Add(new PriceSnapshot
                    {
                        CardId = id, Variant = i % 2 == 0 ? "holofoil" : "reverseHolofoil", Date = Today.AddDays(day - Days),
                        Market = market, Low = market * 0.9m, Mid = market, High = market * 1.4m,
                    });
                }
            }
            await db.SaveChangesAsync();

            // A week-old checkpoint from an earlier model, due to be scored against what happened.
            var past = new PredictionRun
            {
                StartedAt = DateTimeOffset.UtcNow.AddDays(-10), FinishedAt = DateTimeOffset.UtcNow.AddDays(-10), Status = PredictionStatus.Published,
                AsOf = Today.AddDays(-10), HorizonDays = 7, Checkpoint = true,
            };
            db.PredictionRuns.Add(past);
            await db.SaveChangesAsync();
            var then = db.PriceSnapshots.Local.Single(s => s.CardId == "p0-0" && s.Date == Today.AddDays(-10)).Market!.Value;
            db.PricePredictions.Add(new PricePrediction
            {
                RunId = past.Id, CardId = "p0-0", Variant = "holofoil", Current = then, Predicted = then * 1.07m, Low = then, High = then * 1.2m,
                ChangePercent = 7,
            });
            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task Trains_validates_explains_and_scores_itself()
        {
            // No prices yet: nothing to learn from, and the API says so.
            var empty = await Run();
            Assert.Equal(PredictionStatus.InsufficientHistory, empty.Status);
            Assert.Equal(PredictionStatus.InsufficientHistory, (await Get<ModelSummary>("/api/predictions/model")).Status);

            await SeedAsync();
            var result = await Run();

            Assert.Equal(PredictionStatus.Published, result.Status);
            Assert.True(result.Mae < result.BaselineMae, $"MAE {result.Mae} should beat no-change {result.BaselineMae}");
            Assert.Equal(60, result.Predictions);

            var model = await Get<ModelSummary>("/api/predictions/model");
            Assert.Equal(PredictionStatus.Published, model.Status);
            Assert.Equal(Today, model.AsOf);
            Assert.Equal(7, model.HorizonDays);
            Assert.True(model.TypicalErrorPercent < model.NoChangeErrorPercent);
            Assert.NotEmpty(model.Importance);
            Assert.InRange(model.Importance.Sum(f => f.Weight), 99, 101);
            // The old checkpoint was scored against the prices a week later, and its rows cleaned up.
            var scored = Assert.Single(model.TrackRecord);
            Assert.Equal(Today.AddDays(-10), scored.AsOf);
            Assert.Equal(1, scored.Count);

            var up = await Get<PredictionList>("/api/predictions?direction=up&limit=10&minPrice=1");
            Assert.Equal(10, up.Cards.Count);
            Assert.All(up.Cards, c => Assert.StartsWith("Pikachu", c.Name));
            Assert.All(up.Cards, c => Assert.InRange(c.Predicted, c.Low, c.High));

            var down = await Get<PredictionList>("/api/predictions?direction=down&limit=10&minPrice=1");
            Assert.NotEmpty(down.Cards);
            Assert.StartsWith("Bulbasaur", down.Cards[0].Name);

            var card = Assert.Single((await Get<PredictionList>("/api/cards/p0-0/predictions")).Cards);
            Assert.True(card.Predicted > card.Current);
            Assert.InRange(card.Reasons.Count, 1, 3);
            Assert.All(card.Reasons, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
            Assert.Contains(card.Reasons, r => r.EffectPercent > 0);

            Assert.Empty((await Get<PredictionList>("/api/cards/nope-1/predictions")).Cards);
        }
    }
}
