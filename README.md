# PulseLegacy.OnlineStoreSync

Standalone console utility that syncs the legacy ERP's online-store item selection to the
**Zepio Products Update API** (`POST /api/developer/v1/products/bulk-update`).

**Not part of PulseERP.** Deploy it on the client server as a small folder
(one exe + `appsettings.json`) and schedule it with Windows Task Scheduler.

## What it does (each run)

1. Reads `appsettings.json`.
2. Finds the default godown (`fa_godown_master.default_godown = 1`).
3. Reads **all** rows in `fa_ol_items` (the online-store selection table — every selected
   item is sent every run, whether or not anything changed), joined with `fa_item_codes`
   (details) and `fa_item_master` (stock for the default godown + configured period).
4. Stock = `opening_quantity + receipt_quantity − issue_quantity`
   (negative → 0 when `SendNegativeStockAsZero` is true).
5. Validates rows (missing code/name, selling price ≤ 0, MRP < 0 → skipped and logged).
6. Sends products to Zepio in batches of max **100**, Bearer-token auth.
7. For products Zepio accepted, writes back to `fa_ol_items`:
   `uploaded_stock`, `uploaded_mrp`, `uploaded_rate`, `updatedon = GETDATE()`.
   Failed products are **not** marked.

Field mapping: `erp_id`=`item_code`, `track_quantity`=true,
`unit_quantity`=stock, `selling_price`=`retail_rate`, `original_price`=`mrp`,
`is_active`=`active`.
**`name` is deliberately NOT sent** — the customer maintains product names in the
online store, and Zepio only updates fields present in the payload.

## 1. Edit appsettings.json

```json
{
  "ConnectionStrings": {
    "LegacyDb": "Data Source=SERVER\\SQLEXPRESS;Initial Catalog=CLIENT_DB;User ID=sa;Password=password;TrustServerCertificate=True;"
  },
  "Zepio": {
    "Endpoint": "https://soorya.zepio.io/api/developer/v1/products/bulk-update",
    "ApiKey": "PUT_API_KEY_HERE",
    "BranchId": 1,
    "BatchSize": 100,
    "TimeoutSeconds": 60
  },
  "Sync": {
    "PeriodId": 202627,
    "SendNegativeStockAsZero": true,
    "UpdateFaOlItemsAfterSuccess": true,
    "DryRun": true
  }
}
```

- `LegacyDb` — the client's SQL Server connection string.
- `ApiKey` — the Zepio Bearer token. **Only lives in this file, never in code.**
  If the key was ever shared in a document/email, ask Zepio to regenerate it.
- `BranchId` — Zepio branch (default 1).
- `PeriodId` — the current ERP period (e.g. `202627`). **Must be updated when the
  period changes** (auto-detection is planned for a later version).
- `DryRun` — ships as `true` on purpose; see next section.

## 2. Test with DryRun first

With `"DryRun": true` the app reads the database, builds and logs the exact payloads,
but **does not call the API and does not touch `fa_ol_items`**.

```
PulseLegacy.OnlineStoreSync.exe
```

Check `Logs\sync-YYYYMMDD.log`: item count, batches, full JSON payloads, skipped rows.
When the payload looks right, set `"DryRun": false`.

## 3. Run manually

Double-click the exe or run it from a command prompt (working directory doesn't matter —
config and logs resolve next to the exe). Exit codes:

| Code | Meaning |
|------|---------|
| 0 | Completed — all products accepted |
| 2 | Completed — some products/batches failed (see log) |
| 1 | Fatal error — bad config, DB unreachable, etc. |

## 4. Check logs

Daily file, appended per run: `Logs\sync-YYYYMMDD.log`.
Passwords and the API key are never written to the log (key shows as `7|Is********`).

## 5. Publish (build the deployable folder)

On the dev machine ( .NET 8 SDK required):

```
dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -o publish
```

Copy the `publish` folder to the client server (no .NET install needed on the server).
Framework-dependent alternative (smaller, needs .NET 8 runtime on the server):

```
dotnet publish -c Release -o publish-fdd
```

## 6. Schedule with Windows Task Scheduler

1. Task Scheduler → **Create Task…**
2. **General**: name `Zepio Online Store Sync`; select *Run whether user is logged on
   or not*; use a service/admin account that can reach SQL Server.
3. **Triggers**: *New…* → `Daily`, repeat task every `15 minutes` (or 5/30/60) for a
   duration of `1 day`. Adjust to the customer's needs.
4. **Actions**: *New…* → Start a program →
   `C:\PulseLegacy.OnlineStoreSync\PulseLegacy.OnlineStoreSync.exe`
   ("Start in" = the same folder).
5. **Settings**: enable *Stop the task if it runs longer than* `1 hour`;
   *Do not start a new instance* if already running.
6. Save. Right-click → **Run** once and check `Logs\` to confirm.

Task Scheduler records the exit code (0 / 2 / 1) in the task history, so failures are
visible without opening the log.

## Troubleshooting

| Symptom | Check |
|---|---|
| `No default godown found` | `SELECT * FROM fa_godown_master WHERE default_godown = 1` |
| 0 rows read | `fa_ol_items` empty, or wrong `PeriodId` for `fa_item_master` |
| HTTP 401 | Wrong/expired `ApiKey` |
| `Product not found for erp_id …` | The product doesn't exist in Zepio for that branch — the API only **updates** existing products, it never creates them |
| Stock looks wrong | Confirm `PeriodId` and that the default godown is the stock godown |
