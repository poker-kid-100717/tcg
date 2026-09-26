using TcgSignal.Inventory.Domain;

namespace TcgSignal.Inventory.Application;

public interface IInventoryProvider
{
    string Retailer { get; }
    bool IsConfigured { get; }

    Task<ProviderInventoryResult> SearchAsync(InventoryQuery query, CancellationToken cancellationToken);
}
