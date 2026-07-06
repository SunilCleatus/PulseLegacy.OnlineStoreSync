using PulseLegacy.OnlineStoreSync.Models;
using PulseLegacy.OnlineStoreSync.Services;

// ─────────────────────────────────────────────────────────────────────────────
// PulseLegacy.OnlineStoreSync
//
// Standalone console utility: syncs the legacy ERP's online-store item selection
// (fa_ol_items) to the Zepio Products Update API.
//
// NOT part of PulseERP. Deployed on the client server; scheduled with Windows
// Task Scheduler. See README.md.
//
// Exit codes:
//   0 — completed, all products accepted
//   2 — completed, but some products/batches failed (see log)
//   1 — fatal error (bad config, database unreachable, unexpected exception)
// ─────────────────────────────────────────────────────────────────────────────

var baseDir = AppContext.BaseDirectory;
var log     = new FileLogger(baseDir);

try
{
    var config = AppConfig.Load(Path.Combine(baseDir, "appsettings.json"));
    var runner = new SyncRunner(config, log);
    int exit   = await runner.RunAsync();

    log.Info($"Exit code: {exit}");
    return exit;
}
catch (Exception ex)
{
    // Top-level guard: never crash silently — log full details, exit non-zero.
    log.Error("FATAL: sync aborted.", ex);
    log.Info("Exit code: 1");
    return 1;
}
