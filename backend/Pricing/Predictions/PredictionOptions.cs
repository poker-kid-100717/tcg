namespace PokemonTCG.API.Pricing.Predictions;

public class PredictionOptions
{
    public const string SectionName = "Predictions";

    /// <summary>How far ahead the model predicts.</summary>
    public int HorizonDays { get; set; } = 30;

    /// <summary>Days between training samples of the same card. Neighbouring days are nearly identical, so weekly is plenty.</summary>
    public int SampleEveryDays { get; set; } = 7;

    /// <summary>Printings under this market price are left out of training and predictions.</summary>
    public decimal MinPrice { get; set; } = 1m;

    /// <summary>Share of the labelled sample dates (the most recent ones) held out to validate the model.</summary>
    public double ValidationFraction { get; set; } = 0.2;

    public int MinTrainingRows { get; set; } = 1000;
    public int MinValidationRows { get; set; } = 200;

    /// <summary>Training rows are subsampled above this, to fit the container's memory.</summary>
    public int MaxTrainingRows { get; set; } = 150_000;

    /// <summary>The model's validation error must beat "no change" by at least this fraction to be published.</summary>
    public double MinImprovement { get; set; } = 0.02;

    /// <summary>Log returns are clipped to ±this before training, so a data glitch can't dominate the loss.</summary>
    public double LabelClip { get; set; } = 1.5;

    public int Trees { get; set; } = 300;
    public int Leaves { get; set; } = 31;
    public int MinExamplesPerLeaf { get; set; } = 20;
    public double LearningRate { get; set; } = 0.05;
}
