using System.Text.Json;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class DhanyaSyncRunner
{
    private readonly AppConfig _config;
    private readonly LegacyItemReader _reader;
    private readonly DhanyaClient _client;
    private readonly FileLogger _log;

    public DhanyaSyncRunner(
        AppConfig config,
        LegacyItemReader reader,
        DhanyaClient client,
        FileLogger log)
    {
        _config = config;
        _reader = reader;
        _client = client;
        _log = log;
    }

    public async Task<int> RunAsync()
    {
        _log.Info("Dhanya sync started.");
        _log.Info($"DryRun={_config.Sync.DryRun}, BatchSize={_config.Dhanya.BatchSize}, ApiKey={_config.Dhanya.MaskedApiKey}");
        LogConnectionTarget();

        var godown = await _reader.GetDefaultGodownAsync();
        if (godown == null)
        {
            _log.Error("No default godown found (fa_godown_master.default_godown = 1). Cannot continue.");
            return 1;
        }

        int periodId = await ResolvePeriodIdAsync();
        var items = await _reader.ReadItemsAsync(godown.Value.GodownCode, periodId);
        _log.Info($"fa_ol_items rows read for Dhanya: {items.Count} (period {periodId})");

        if (items == null || items.Count == 0)
        {
            _log.Warn("Nothing to sync for Dhanya.");
            return SyncRunner.ExitOk;
        }

        var products = BuildProducts(items);
        if (products.Count == 0)
        {
            _log.Warn("All rows were skipped by validation for Dhanya.");
            return SyncRunner.ExitCompletedWithFailures;
        }

        var batches = Chunk(products, _config.Dhanya.BatchSize);
        _log.Info($"Dhanya valid products: {products.Count} in {batches.Count} batch(es).");

        if (_config.Sync.DryRun)
        {
            LogDryRun(batches);
            _log.Info("Dhanya DryRun complete - no API call was made.");
            return SyncRunner.ExitOk;
        }

        bool anyFailures = false;
        int totalSent = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            _log.Info($"Dhanya batch {i + 1}/{batches.Count}: sending {batch.Count} product(s)...");

            var taskId = await _client.SendBatchAsync(batch);
            if (string.IsNullOrWhiteSpace(taskId))
            {
                anyFailures = true;
                continue;
            }

            bool completed = await PollTaskResultAsync(taskId);
            if (!completed)
                anyFailures = true;
            else
                totalSent += batch.Count;
        }

        _log.Info($"Dhanya sync finished. Sent OK: {totalSent}.");
        return anyFailures ? SyncRunner.ExitCompletedWithFailures : SyncRunner.ExitOk;
    }

    private List<DhanyaProductDto> BuildProducts(List<OnlineStoreItem> items)
    {
        var products = new List<DhanyaProductDto>();
        var seenErpIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        foreach (var item in items)
        {
            var resolvedStock = item.FixedStock > 0 ? item.FixedStock : item.RawStock;
            if (_config.Sync.SendNegativeStockAsZero && resolvedStock < 0)
                resolvedStock = 0;

            var resolvedRate = item.FixedRate > 0 ? item.FixedRate : item.SellingPrice;
            var resolvedMrp = item.FixedMrp > 0 ? item.FixedMrp : item.OriginalPrice;

            string? reason =
                string.IsNullOrWhiteSpace(item.ErpId) ? "missing item_code" :
                resolvedRate <= 0 ? $"selling_price {resolvedRate} <= 0" :
                resolvedMrp < 0 ? $"original_price {resolvedMrp} < 0" :
                null;

            if (reason != null)
            {
                _log.Warn($"DHANYA SKIPPED ol_item_id={item.OlItemId} erp_id='{item.ErpId}': {reason}");
                skipped++;
                continue;
            }

            if (!seenErpIds.Add(item.ErpId))
            {
                _log.Warn($"DHANYA SKIPPED ol_item_id={item.OlItemId}: duplicate erp_id '{item.ErpId}' already queued in this run.");
                skipped++;
                continue;
            }

            products.Add(new DhanyaProductDto
            {
                erpId = item.ErpId,
                MRP = resolvedMrp,
                selling_price = resolvedRate,
                // TODO: re-enable DiscountSellingPrice when needed
                // discount_selling_price = 0,
                Qty = (int)Math.Max(0, resolvedStock)
            });
        }

        if (skipped > 0)
            _log.Warn($"{skipped} row(s) skipped by validation during Dhanya sync.");

        return products;
    }

    private void LogDryRun(List<List<DhanyaProductDto>> batches)
    {
        for (int i = 0; i < batches.Count; i++)
        {
            var request = new DhanyaInventoryRequest { products = batches[i] };
            _log.Info($"[DHANYA DRY-RUN] Batch {i + 1}/{batches.Count} payload ({batches[i].Count} products):");
            _log.Info(JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private async Task<int> ResolvePeriodIdAsync()
    {
        if (!_config.Sync.AutoDetectPeriod)
        {
            _log.Info($"Using configured PeriodId {_config.Sync.PeriodId} (auto-detect disabled).");
            return _config.Sync.PeriodId;
        }

        var detected = await _reader.GetCurrentPeriodIdAsync();
        if (detected.HasValue)
        {
            _log.Info($"Auto-detected current period from fa_periods: {detected.Value}.");
            return detected.Value;
        }

        if (_config.Sync.PeriodId > 0)
        {
            _log.Warn($"Could not auto-detect a current period from fa_periods; falling back to configured PeriodId {_config.Sync.PeriodId}.");
            return _config.Sync.PeriodId;
        }

        throw new InvalidOperationException("Could not auto-detect a current period from fa_periods and no fallback Sync:PeriodId is configured.");
    }

    private async Task<bool> PollTaskResultAsync(string taskId)
    {
        for (int attempt = 1; attempt <= _config.Dhanya.MaxPollAttempts; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(_config.Dhanya.TaskPollIntervalSeconds));

            var result = await _client.GetTaskStatusAsync(taskId);
            if (result == null)
                continue;

            if (string.Equals(result.status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                _log.Info($"Dhanya task {taskId} completed | Updated: {result.summary?.updated ?? 0} | Skipped: {result.summary?.skipped ?? 0}");
                return true;
            }

            if (string.Equals(result.status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                _log.Error($"Dhanya task {taskId} failed.");
                return false;
            }

            _log.Info($"Dhanya task {taskId} status: {result.status} (attempt {attempt}/{_config.Dhanya.MaxPollAttempts}).");
        }

        _log.Warn($"Dhanya task {taskId} polling timed out.");
        return false;
    }

    private void LogConnectionTarget()
    {
        try
        {
            var b = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_config.ConnectionStrings.LegacyDb);
            _log.Info($"Database: {b.DataSource} / {b.InitialCatalog}");
        }
        catch
        {
            _log.Warn("Could not parse connection string for logging (connection will still be attempted).");
        }
    }

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var result = new List<List<T>>();
        for (int i = 0; i < source.Count; i += size)
            result.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return result;
    }
}
