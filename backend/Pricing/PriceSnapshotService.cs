using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokemonTCG.API.Data;
using PokemonTCG.API.Market.Providers;

namespace PokemonTCG.API.Pricing;

public class SnapshotOptions
{
    public const string SectionName = "Snapshots";

    /// <summary>Prices below this market value aren't stored; they're mostly bulk commons.</summary>
    public decimal MinMarketPrice { get; set; } = 0.50m;

    /// <summary>Snapshots older than this are deleted at the end of each run.</summary>
    public int RetentionDays { get; set; } = 400;

    /// <summary>Pause between upstream pages, to stay well inside the API's rate limit.</summary>
    public int PageDelayMilliseconds { get; set; } = 250;
}

/// <summary>One snapshot run at a time per API instance; a second trigger while one is running is refused.</summary>
public sealed class SnapshotGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public record SnapshotResult(int RunId, SnapshotStatus Status, int CardsSeen, int PricesWritten, string? Error);

/// <summary>
/// Collects today's TCGplayer prices for every card into price_snapshots.
/// Cloudflare runs it once a day through a Cron Trigger (see worker/index.ts).
/// Each page is written with one bulk upsert per table, so a run is a few
/// dozen round trips rather than tens of thousands, and rerunning a day
/// overwrites that day's rows instead of duplicating them.
/// </summary>
public class PriceSnapshotService(
    SnapshotGate gate,
    IEnumerable<IMarketDataProvider> providers,
    AppDbContext db,
    SnapshotOptions options,
    TimeProvider clock,
    ILogger<PriceSnapshotService> logger)
{
    public static readonly JsonSerializerOptions ProviderJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };


    public async Task<SnapshotResult?> RunAsync(CancellationToken cancellationToken)
    {
        if (!await gate.Semaphore.WaitAsync(0, cancellationToken)) return null;
        try
        {
            return await RunExclusiveAsync(cancellationToken);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    private async Task<SnapshotResult> RunExclusiveAsync(CancellationToken cancellationToken)
    {
        var run = new SnapshotRun { StartedAt = clock.GetUtcNow(), Status = SnapshotStatus.Running };
        db.SnapshotRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            // The whole run is one transaction: a run that fails part-way leaves no half-recorded day behind
            // (the price model and movers would otherwise read it as a complete one).
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var statuses = new List<ProviderRunStatus>();

            // The primary provider fills the catalog and the day's reference prices. If it fails, the run fails and
            // nothing is recorded: yesterday's prices stay, marked with their own date, rather than a partial day.
            var ordered = providers.OrderBy(p => p.Role).ToList();
            var primary = ordered.FirstOrDefault(p => p.Role == ProviderRole.Primary && p.IsEnabled)
                ?? throw new InvalidOperationException("No primary market data provider is enabled.");
            var primaryResult = await primary.IngestAsync(today, cancellationToken);
            run.CardsSeen = primaryResult.CardsSeen;
            run.PricesWritten = primaryResult.PricesWritten;
            statuses.Add(new ProviderRunStatus(primary.Id, ProviderRunStatus.Succeeded, primaryResult.PricesWritten, null));

            // Overlays refine the day's prices. One that fails is rolled back to a savepoint and reported; the primary
            // provider's prices stand under the primary provider's name, never relabelled as the overlay's.
            foreach (var overlay in ordered.Where(p => p.Role == ProviderRole.Overlay))
            {
                if (!overlay.IsEnabled)
                {
                    statuses.Add(new ProviderRunStatus(overlay.Id, ProviderRunStatus.Disabled, 0, null));
                    continue;
                }
                await transaction.CreateSavepointAsync("overlay", cancellationToken);
                try
                {
                    var result = await overlay.IngestAsync(today, cancellationToken);
                    run.PricesWritten += result.PricesWritten;
                    statuses.Add(new ProviderRunStatus(overlay.Id, ProviderRunStatus.Succeeded, result.PricesWritten, null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await transaction.RollbackToSavepointAsync("overlay", cancellationToken);
                    logger.LogWarning(ex, "Market data provider {Provider} failed; its prices were not recorded", overlay.Id);
                    statuses.Add(new ProviderRunStatus(overlay.Id, ProviderRunStatus.Failed, 0, "The provider didn't respond as expected."));
                }
            }
            run.ProviderResults = JsonSerializer.Serialize(statuses, ProviderJson);

            var cutoff = today.AddDays(-options.RetentionDays);
            await db.PriceSnapshots.Where(s => s.Date < cutoff).ExecuteDeleteAsync(cancellationToken);

            // The run's success commits with its data: a crash after the commit can't leave complete prices behind a
            // run still marked Running (which would hold back the prediction refresh).
            run.Status = SnapshotStatus.Succeeded;
            run.FinishedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Price snapshot run {RunId} failed after {Cards} cards", run.Id, run.CardsSeen);
            // Everything written was rolled back, so nothing was recorded.
            run.CardsSeen = 0;
            run.PricesWritten = 0;
            run.Status = SnapshotStatus.Failed;
            run.Error = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        }

        run.FinishedAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Price snapshot run {RunId} {Status}: {Cards} cards, {Prices} prices",
            run.Id, run.Status, run.CardsSeen, run.PricesWritten);
        return new SnapshotResult(run.Id, run.Status, run.CardsSeen, run.PricesWritten, run.Error);
    }
}
