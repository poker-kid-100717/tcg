namespace TcgSignal.Inventory;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    public string BestBuyApiKey { get; set; } = "";
    public int MaxProductsPerRefresh { get; set; } = 25;
    public int CacheSeconds { get; set; } = 120;
}
