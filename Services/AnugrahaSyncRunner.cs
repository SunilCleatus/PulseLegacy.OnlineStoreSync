using System.Text.Json;
using Microsoft.Data.SqlClient;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class AnugrahaSyncRunner
{
    private readonly AppConfig _config;
    private readonly LegacyItemReader _reader;
    private readonly FileLogger _log;
    private readonly AnugrahaStateStore _stateStore;
    private readonly ChangeDetector _changeDetector = new();

    public AnugrahaSyncRunner(
        AppConfig config,
        LegacyItemReader reader,
        FileLogger log,
        AnugrahaStateStore stateStore)
    {
        _config = config;
        _reader = reader;
        _log = log;
        _stateStore = stateStore;
    }

    public async Task<int> RunAsync()
    {
        _log.Info("Anugraha sync started.");
        _log.Info($"DryRun={_config.Sync.DryRun}, BatchSize={_config.Anugraha.BatchSize}, ApiKey={_config.Anugraha.MaskedApiKey}");
        LogConnectionTarget();

        var godown = await _reader.GetDefaultGodownAsync();
        if (godown == null)
        {
            _log.Error("No default godown found (fa_godown_master.default_godown = 1). Cannot continue.");
            return 1;
        }

        int periodId = await ResolvePeriodIdAsync();
        var rows = await _reader.ReadItemsForAnugrahaAsync(godown.Value.GodownCode, periodId);
        _log.Info($"fa_ol_items rows read for Anugraha: {rows.Count} (period {periodId})");

        if (rows.Count == 0)
        {
            _log.Warn("Nothing to sync for Anugraha.");
            return SyncRunner.ExitOk;
        }

        var (products, byErpId, skipped) = BuildProducts(rows);
        if (skipped > 0)
            _log.Warn($"{skipped} row(s) skipped by validation during Anugraha sync.");

        if (products.Count == 0)
        {
            _log.Warn("All rows were skipped by validation during Anugraha sync.");
            return SyncRunner.ExitOk;
        }

        var batches = Chunk(products, _config.Anugraha.BatchSize);
        _log.Info($"Anugraha products to send: {products.Count} in {batches.Count} batch(es).");

        SaveRequestArchive(batches);

        if (_config.Sync.DryRun)
        {
            LogDryRun(batches);
            _log.Info("Anugraha DryRun complete - no API call was made, fa_ol_items not updated.");
            return SyncRunner.ExitOk;
        }

        using var client = new AnugrahaClient(
            _config.Anugraha.Endpoint,
            _config.Anugraha.ApiKey,
            _config.Anugraha.TimeoutSeconds);

        bool anyFailures = false;
        int totalSent = 0;
        int totalFailed = 0;
        int totalMarked = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            _log.Info($"Anugraha batch {i + 1}/{batches.Count}: sending {batches[i].Count} product(s)...");

            var result = await client.SendBatchAsync(new AnugrahaProductUpdateRequest
            {
                BranchId = _config.Anugraha.BranchId,
                Products = batches[i],
            });

            if (result.TransportError != null)
            {
                _log.Error($"Anugraha batch {i + 1}: transport error - {result.TransportError}");
                anyFailures = true;
                totalFailed += batches[i].Count;
                continue;
            }

            _log.Info($"Anugraha batch {i + 1}: HTTP {result.StatusCode}. Response: {Truncate(result.RawBody, 500)}");

            var failedErpIds = new HashSet<string>(
                result.Parsed?.FailedProducts
                    .Select(f => (f.ErpId ?? string.Empty).Trim())
                    .Where(s => s.Length > 0) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (!result.HttpSuccess && failedErpIds.Count == 0)
            {
                _log.Error($"Anugraha batch {i + 1}: request failed. No rows in this batch will be marked as uploaded.");
                anyFailures = true;
                totalFailed += batches[i].Count;
                continue;
            }

            foreach (var failed in result.Parsed?.FailedProducts ?? Enumerable.Empty<AnugrahaProductUpdateResponse.FailedProduct>())
                _log.Warn($"Anugraha batch {i + 1}: FAILED erp_id={failed.ErpId}: {failed.Message}");

            var succeeded = batches[i].Where(p => !failedErpIds.Contains(p.ErpId)).ToList();
            totalSent += succeeded.Count;
            totalFailed += failedErpIds.Count;
            if (failedErpIds.Count > 0) anyFailures = true;

            if (_config.Sync.UpdateFaOlItemsAfterSuccess && succeeded.Count > 0)
            {
                var updates = succeeded
                    .Where(p => byErpId.ContainsKey(p.ErpId))
                    .Select(p =>
                    {
                        var src = byErpId[p.ErpId];
                        return (src.OlItemId, p.UnitQuantity, p.OriginalPrice, p.SellingPrice);
                    });

                try
                {
                    int marked = await _reader.MarkUploadedAsync(updates);
                    totalMarked += marked;
                    _log.Info($"Anugraha batch {i + 1}: {marked} fa_ol_items row(s) marked as uploaded.");
                }
                catch (SqlException ex)
                {
                    _log.Error($"Anugraha batch {i + 1}: failed to update fa_ol_items uploaded_* columns. Local bookkeeping is stale for this batch.", ex);
                    anyFailures = true;
                }
            }
        }

        _log.Info($"Anugraha sync finished. Sent OK: {totalSent}, failed: {totalFailed}, fa_ol_items rows marked: {totalMarked}.");
        return anyFailures ? SyncRunner.ExitCompletedWithFailures : SyncRunner.ExitOk;
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

    private (List<AnugrahaProductDto> Products, Dictionary<string, OnlineStoreItem> ByErpId, int Skipped)
        BuildProducts(List<OnlineStoreItem> rows)
    {
        var products = new List<AnugrahaProductDto>();
        var byErpId = new Dictionary<string, OnlineStoreItem>(StringComparer.OrdinalIgnoreCase);
        int skipped = 0;

        foreach (var row in rows)
        {
            ResolveRowValues(row);

            string? reason =
                string.IsNullOrWhiteSpace(row.ErpId) ? "missing item_code" :
                row.ResolvedRate <= 0 ? $"selling_price {row.ResolvedRate} <= 0" :
                row.ResolvedMrp < 0 ? $"original_price {row.ResolvedMrp} < 0" :
                null;

            if (reason != null)
            {
                _log.Warn($"ANUGRAHA SKIPPED ol_item_id={row.OlItemId} erp_id='{row.ErpId}': {reason}");
                skipped++;
                continue;
            }

            if (byErpId.ContainsKey(row.ErpId))
            {
                _log.Warn($"ANUGRAHA SKIPPED ol_item_id={row.OlItemId}: duplicate erp_id '{row.ErpId}' already queued in this run.");
                skipped++;
                continue;
            }

            byErpId[row.ErpId] = row;
            products.Add(new AnugrahaProductDto
            {
                ErpId = row.ErpId,
                TrackQuantity = true,
                UnitQuantity = row.ResolvedStock,
                SellingPrice = row.ResolvedRate,
                OriginalPrice = row.ResolvedMrp,
                IsActive = row.IsActive,
            });
        }

        return (products, byErpId, skipped);
    }

    private void ResolveRowValues(OnlineStoreItem row)
    {
        if (row.FixedStock > 0)
        {
            row.ResolvedStock = row.FixedStock;
        }
        else
        {
            row.ResolvedStock = row.RawStock;
            if (row.ResolvedStock < 0 && _config.Sync.SendNegativeStockAsZero)
                row.ResolvedStock = 0;
        }

        row.ResolvedRate = row.FixedRate > 0 ? row.FixedRate : row.SellingPrice;
        row.ResolvedMrp = row.FixedMrp > 0 ? row.FixedMrp : row.OriginalPrice;
    }

    private void LogDryRun(List<List<AnugrahaProductDto>> batches)
    {
        for (int i = 0; i < batches.Count; i++)
        {
            var payload = new AnugrahaProductUpdateRequest
            {
                BranchId = _config.Anugraha.BranchId,
                Products = batches[i],
            };

            _log.Info($"[ANUGRAHA DRY-RUN] Batch {i + 1}/{batches.Count} payload ({batches[i].Count} products):");
            _log.Info(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private void SaveRequestArchive(List<List<AnugrahaProductDto>> batches)
    {
        var snapshot = new
        {
            generatedOn = DateTimeOffset.Now,
            dryRun = _config.Sync.DryRun,
            totalProducts = batches.Sum(b => b.Count),
            batchCount = batches.Count,
            requests = batches.Select(b => new AnugrahaProductUpdateRequest
            {
                BranchId = _config.Anugraha.BranchId,
                Products = b,
            }),
        };

        _log.SaveRequestSnapshot(JsonSerializer.Serialize(
            snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LogConnectionTarget()
    {
        try
        {
            var b = new SqlConnectionStringBuilder(_config.ConnectionStrings.LegacyDb);
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

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...(truncated)";
}
