using System.Text.Json;
using Microsoft.Data.SqlClient;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

/// <summary>
/// Orchestrates one sync run:
///   read config → find default godown → read all fa_ol_items → validate →
///   batch (max 100) → POST to Zepio → mark successful rows in fa_ol_items.
///
/// Exit codes (returned to Program/Task Scheduler):
///   0 — run completed, every product accepted by Zepio
///   2 — run completed, but some batches/products failed (details in the log)
///   1 — fatal error (config, database, unexpected exception) — set by Program.cs
/// </summary>
public sealed class SyncRunner
{
    public const int ExitOk = 0;
    public const int ExitCompletedWithFailures = 2;

    private readonly AppConfig  _config;
    private readonly FileLogger _log;

    public SyncRunner(AppConfig config, FileLogger log)
    {
        _config = config;
        _log    = log;
    }

    public async Task<int> RunAsync()
    {
        _log.Info("──────────────────────────────────────────────────");
        string periodMode = _config.Sync.AutoDetectPeriod
            ? $"auto (fallback {_config.Sync.PeriodId})"
            : $"configured {_config.Sync.PeriodId}";
        _log.Info($"Sync started. DryRun={_config.Sync.DryRun}, " +
                  $"Period={periodMode}, BranchId={_config.Zepio.BranchId}, " +
                  $"ApiKey={_config.MaskedApiKey}");
        LogConnectionTarget();

        var reader = new LegacyItemReader(_config.ConnectionStrings.LegacyDb);

        // 1. Default godown ──────────────────────────────────────────────────
        var godown = await reader.GetDefaultGodownAsync();
        if (godown == null)
        {
            _log.Error("No default godown found (fa_godown_master.default_godown = 1). Cannot continue.");
            return 1;
        }
        _log.Info($"Default godown: {godown.Value.GodownName} ({godown.Value.GodownCode})");

        // 1b. Resolve the accounting period ───────────────────────────────────
        int periodId;
        if (_config.Sync.AutoDetectPeriod)
        {
            var detected = await reader.GetCurrentPeriodIdAsync();
            if (detected.HasValue)
            {
                periodId = detected.Value;
                _log.Info($"Auto-detected current period from fa_periods: {periodId}.");
            }
            else if (_config.Sync.PeriodId > 0)
            {
                periodId = _config.Sync.PeriodId;
                _log.Warn($"Could not auto-detect a current period from fa_periods; " +
                          $"falling back to configured PeriodId {periodId}.");
            }
            else
            {
                _log.Error("Could not auto-detect a current period from fa_periods and no " +
                           "fallback Sync:PeriodId is configured. Cannot continue.");
                return 1;
            }
        }
        else
        {
            periodId = _config.Sync.PeriodId;
            _log.Info($"Using configured PeriodId {periodId} (auto-detect disabled).");
        }

        // 2. Read ALL selected online items (fa_ol_items) ─────────────────────
        var rows = await reader.ReadItemsAsync(godown.Value.GodownCode, periodId);
        _log.Info($"fa_ol_items rows read: {rows.Count} (period {periodId})");

        if (rows.Count == 0)
        {
            _log.Warn("Nothing to sync — fa_ol_items produced no rows.");
            return ExitOk;
        }

        // 3. Validate + convert ───────────────────────────────────────────────
        var (products, byErpId) = BuildProducts(rows);
        if (products.Count == 0)
        {
            _log.Warn("All rows were skipped by validation — nothing to send.");
            return ExitCompletedWithFailures;
        }

        // 4. Batch (Zepio max 100 per request) ────────────────────────────────
        var batches = Chunk(products, _config.Zepio.BatchSize);
        _log.Info($"Products to send: {products.Count} in {batches.Count} batch(es) " +
                  $"of up to {_config.Zepio.BatchSize}.");

        // Archive the EXACT payload (incl. unit_quantity) for diagnostics — newest 3 kept.
        SaveRequestArchive(batches);

        // 5. DryRun: log what would be sent, then stop ────────────────────────
        if (_config.Sync.DryRun)
        {
            LogDryRun(batches);
            _log.Info("DryRun complete — no API call was made, fa_ol_items not updated.");
            return ExitOk;
        }

        // 6. Send batches ─────────────────────────────────────────────────────
        using var client = new ZepioClient(
            _config.Zepio.Endpoint, _config.Zepio.ApiKey, _config.Zepio.TimeoutSeconds);

        bool anyFailures  = false;
        int  totalOk      = 0;
        int  totalFailed  = 0;
        int  totalMarked  = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];
            _log.Info($"Batch {i + 1}/{batches.Count}: sending {batch.Count} product(s)…");

            var result = await client.SendBatchAsync(new ZepioProductUpdateRequest
            {
                BranchId = _config.Zepio.BranchId,
                Products = batch,
            });

            if (result.TransportError != null)
            {
                // Network error / timeout: outcome unknown — do NOT mark anything uploaded.
                _log.Error($"Batch {i + 1}: transport error — {result.TransportError}. " +
                           "No rows in this batch will be marked as uploaded.");
                anyFailures = true;
                totalFailed += batch.Count;
                continue;
            }

            _log.Info($"Batch {i + 1}: HTTP {result.StatusCode}. " +
                      $"Response: {Truncate(result.RawBody, 500)}");

            // Failed products (present in both success and failure envelopes).
            var failedErpIds = new HashSet<string>(
                result.Parsed?.FailedProducts
                    .Select(f => (f.ErpId ?? string.Empty).Trim())
                    .Where(s => s.Length > 0) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            if (!result.HttpSuccess && failedErpIds.Count == 0)
            {
                // Whole batch rejected without a per-product list (auth error, 5xx, …).
                _log.Error($"Batch {i + 1}: request failed (HTTP {result.StatusCode}) with no " +
                           "per-product detail. No rows in this batch will be marked as uploaded.");
                anyFailures = true;
                totalFailed += batch.Count;
                continue;
            }

            foreach (var f in result.Parsed?.FailedProducts ?? Enumerable.Empty<ZepioProductUpdateResponse.ZepioFailedProduct>())
                _log.Warn($"Batch {i + 1}: FAILED erp_id={f.ErpId}: {f.Message}");

            // Successful = sent − failed (per API doc: partial success updates the rest).
            var succeeded = batch.Where(p => !failedErpIds.Contains(p.ErpId)).ToList();
            totalOk     += succeeded.Count;
            totalFailed += failedErpIds.Count;
            if (failedErpIds.Count > 0) anyFailures = true;

            // 7. Mark uploaded_* in fa_ol_items for the successful products only.
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
                    int marked = await reader.MarkUploadedAsync(updates);
                    totalMarked += marked;
                    _log.Info($"Batch {i + 1}: {marked} fa_ol_items row(s) marked as uploaded.");
                }
                catch (SqlException ex)
                {
                    // The store WAS updated; only the local bookkeeping failed.
                    _log.Error($"Batch {i + 1}: failed to update fa_ol_items uploaded_* columns. " +
                               "Zepio was updated; local bookkeeping is stale for this batch.", ex);
                    anyFailures = true;
                }
            }
        }

        _log.Info($"Sync finished. Sent OK: {totalOk}, failed: {totalFailed}, " +
                  $"fa_ol_items rows marked: {totalMarked}.");
        return anyFailures ? ExitCompletedWithFailures : ExitOk;
    }

    // ── Validation + conversion ───────────────────────────────────────────────

    private (List<ZepioProductDto> Products, Dictionary<string, OnlineStoreItem> ByErpId)
        BuildProducts(List<OnlineStoreItem> rows)
    {
        var products = new List<ZepioProductDto>();
        var byErpId  = new Dictionary<string, OnlineStoreItem>(StringComparer.OrdinalIgnoreCase);
        int skipped  = 0;

        foreach (var row in rows)
        {
            // Per-item overrides from fa_ol_items: a fixed value (> 0) wins over the live
            // figure, so fluctuating items (vegetables, ready-to-cook) can show a steady
            // stock/price online. 0 means "not fixed — use the live value".

            // Stock: fixed wins; otherwise live stock (with negative→0 per config).
            decimal qty;
            if (row.FixedStock > 0)
            {
                qty = row.FixedStock;
            }
            else
            {
                qty = row.RawStock;
                if (qty < 0 && _config.Sync.SendNegativeStockAsZero) qty = 0;
            }

            decimal sellingPrice  = row.FixedRate > 0 ? row.FixedRate : row.SellingPrice;
            decimal originalPrice = row.FixedMrp  > 0 ? row.FixedMrp  : row.OriginalPrice;

            // NOTE: item_name is NOT validated or sent — the customer maintains product
            // names in the online store; the sync must not overwrite them.
            // Validation runs on the RESOLVED values (after fixed overrides).
            string? reason =
                string.IsNullOrWhiteSpace(row.ErpId)  ? "missing item_code" :
                sellingPrice <= 0                     ? $"selling_price {sellingPrice} <= 0" :
                originalPrice < 0                     ? $"original_price {originalPrice} < 0" :
                null;

            if (reason != null)
            {
                _log.Warn($"SKIPPED ol_item_id={row.OlItemId} erp_id='{row.ErpId}': {reason}");
                skipped++;
                continue;
            }

            if (byErpId.ContainsKey(row.ErpId))
            {
                _log.Warn($"SKIPPED ol_item_id={row.OlItemId}: duplicate erp_id '{row.ErpId}' " +
                          "already queued in this run.");
                skipped++;
                continue;
            }

            if (row.FixedStock > 0 || row.FixedRate > 0 || row.FixedMrp > 0)
                _log.Info($"erp_id={row.ErpId}: fixed override applied " +
                          $"(stock={(row.FixedStock > 0 ? row.FixedStock.ToString() : "live")}, " +
                          $"rate={(row.FixedRate > 0 ? row.FixedRate.ToString() : "live")}, " +
                          $"mrp={(row.FixedMrp > 0 ? row.FixedMrp.ToString() : "live")}).");

            byErpId[row.ErpId] = row;
            products.Add(new ZepioProductDto
            {
                ErpId         = row.ErpId,
                TrackQuantity = true,
                UnitQuantity  = qty,
                SellingPrice  = sellingPrice,
                OriginalPrice = originalPrice,
                IsActive      = row.IsActive,
            });
        }

        if (skipped > 0)
            _log.Warn($"{skipped} row(s) skipped by validation; {products.Count} product(s) remain.");

        return (products, byErpId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<List<ZepioProductDto>> Chunk(List<ZepioProductDto> source, int size)
    {
        var result = new List<List<ZepioProductDto>>();
        for (int i = 0; i < source.Count; i += size)
            result.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return result;
    }

    /// <summary>
    /// Writes the exact request bodies for this run to Logs/request-*.json (newest 3 kept).
    /// `requests[]` elements are the precise POST bodies sent to Zepio — including
    /// unit_quantity — so we can confirm what the store received.
    /// </summary>
    private void SaveRequestArchive(List<List<ZepioProductDto>> batches)
    {
        var snapshot = new
        {
            generatedOn   = DateTimeOffset.Now,
            dryRun        = _config.Sync.DryRun,
            branchId      = _config.Zepio.BranchId,
            totalProducts = batches.Sum(b => b.Count),
            batchCount    = batches.Count,
            requests      = batches.Select(b => new ZepioProductUpdateRequest
            {
                BranchId = _config.Zepio.BranchId,
                Products = b,
            }),
        };
        _log.SaveRequestSnapshot(JsonSerializer.Serialize(
            snapshot, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LogDryRun(List<List<ZepioProductDto>> batches)
    {
        for (int i = 0; i < batches.Count; i++)
        {
            var payload = new ZepioProductUpdateRequest
            {
                BranchId = _config.Zepio.BranchId,
                Products = batches[i],
            };
            _log.Info($"[DRY-RUN] Batch {i + 1}/{batches.Count} payload " +
                      $"({batches[i].Count} products):");
            _log.Info(JsonSerializer.Serialize(
                payload, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    /// <summary>Logs server + database only — never the credentials.</summary>
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

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…(truncated)";
}
