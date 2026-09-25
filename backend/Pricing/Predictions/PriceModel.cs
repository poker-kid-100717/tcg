using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace PokemonTCG.API.Pricing.Predictions;

public sealed class ModelInput
{
    [VectorType(PriceFeatures.Count)]
    public float[] Features { get; set; } = [];

    public float Label { get; set; }
}

public sealed class ModelOutput
{
    public float Score { get; set; }

    [VectorType(PriceFeatures.Count)]
    public float[] FeatureContributions { get; set; } = [];
}

/// <summary>
/// Gradient-boosted regression trees (ML.NET FastTree) predicting the log of a card's price ratio over the
/// horizon. Trees suit this data: the features mix scales (prices, ages, ratios, flags) and interact
/// (a 1st Edition holo from an old set behaves nothing like a reverse holo from last month's set), and a
/// tree ensemble can report each feature's contribution to a single prediction.
/// </summary>
public sealed class PriceModel
{
    private readonly MLContext _ml;
    private readonly RegressionPredictionTransformer<FastTreeRegressionModelParameters> _model;
    private readonly ITransformer _explainer;

    private PriceModel(MLContext ml, RegressionPredictionTransformer<FastTreeRegressionModelParameters> model, ITransformer explainer)
    {
        _ml = ml;
        _model = model;
        _explainer = explainer;
    }

    public static PriceModel Train(IReadOnlyList<ModelInput> rows, PredictionOptions options)
    {
        var ml = new MLContext(seed: 7);
        var data = ml.Data.LoadFromEnumerable(rows);
        var trainer = ml.Regression.Trainers.FastTree(new FastTreeRegressionTrainer.Options
        {
            NumberOfTrees = options.Trees,
            NumberOfLeaves = options.Leaves,
            MinimumExampleCountPerLeaf = options.MinExamplesPerLeaf,
            LearningRate = options.LearningRate,
            FeatureFraction = 0.8,
            // Each tree sees a bag of rows as well as a subset of features, which keeps a few very active cards from dominating.
            BaggingSize = 1,
            BaggingExampleFraction = 0.7,
            NumberOfThreads = 1,
        });
        var model = trainer.Fit(data);
        var explainer = ml.Transforms
            .CalculateFeatureContribution(model, numberOfPositiveContributions: 4, numberOfNegativeContributions: 4, normalize: false)
            .Fit(model.Transform(data));
        return new PriceModel(ml, model, explainer);
    }

    public IReadOnlyList<ModelOutput> Predict(IReadOnlyList<ModelInput> rows)
    {
        if (rows.Count == 0) return [];
        var scored = _explainer.Transform(_model.Transform(_ml.Data.LoadFromEnumerable(rows)));
        return _ml.Data.CreateEnumerable<ModelOutput>(scored, reuseRowObject: false).ToList();
    }

    /// <summary>Split-gain importance of each feature, normalized to sum to 1.</summary>
    public double[] Importance()
    {
        VBuffer<float> weights = default;
        _model.Model.GetFeatureWeights(ref weights);
        var dense = weights.DenseValues().Select(w => (double)w).ToArray();
        var total = dense.Sum();
        return total > 0 ? dense.Select(w => w / total).ToArray() : dense;
    }
}
