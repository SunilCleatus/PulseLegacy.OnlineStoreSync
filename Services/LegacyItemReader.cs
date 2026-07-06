using Microsoft.Data.SqlClient;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

/// <summary>
/// Reads the online-store item selection from the legacy ERP database.
///
/// Sources:
///   fa_godown_master — default_godown = 1 identifies the stock godown.
///   fa_ol_items      — the selection table: ONLY items present here are sent,
///                      and ALL of them are sent on every run (customer requirement —
///                      no changed-only filtering).
///   fa_item_codes    — item details (item_code, item_name, retail_rate, mrp, active).
///   fa_item_master   — stock per (item_no, godown_code, period_id):
///                      opening_quantity + receipt_quantity - issue_quantity.
/// </summary>
public sealed class LegacyItemReader
{
    private readonly string _connectionString;

    public LegacyItemReader(string connectionString)
        => _connectionString = connectionString;

    /// <summary>Returns the default godown, or null when none is flagged.</summary>
    public async Task<(Guid GodownCode, string GodownName)?> GetDefaultGodownAsync()
    {
        const string sql = @"
            SELECT TOP 1 godown_code, godown_name
            FROM dbo.fa_godown_master
            WHERE default_godown = 1;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync()) return null;
        return (reader.GetGuid(0), reader.IsDBNull(1) ? "(unnamed)" : reader.GetString(1));
    }

    /// <summary>
    /// Reads all fa_ol_items rows with their item details and raw stock for the
    /// given godown/period. The raw (possibly negative) stock is returned;
    /// zero-clamping is a configuration decision applied by the caller.
    /// </summary>
    public async Task<List<OnlineStoreItem>> ReadItemsAsync(Guid defaultGodown, int periodId)
    {
        const string sql = @"
            SELECT
                oi.id                                   AS ol_item_id,
                ic.item_no,
                ic.item_code                            AS erp_id,
                ic.item_name                            AS name,
                ISNULL(im.opening_quantity, 0)
                  + ISNULL(im.receipt_quantity, 0)
                  - ISNULL(im.issue_quantity, 0)        AS raw_stock,
                ic.retail_rate                          AS selling_price,
                ic.mrp                                  AS original_price,
                ic.active                               AS is_active
            FROM dbo.fa_ol_items oi
            INNER JOIN dbo.fa_item_codes ic
                ON ic.item_no = oi.item_no
            LEFT JOIN dbo.fa_item_master im
                ON  im.item_no     = ic.item_no
                AND im.godown_code = @DefaultGodown
                AND im.period_id   = @PeriodId
            WHERE ic.item_code IS NOT NULL
              AND LTRIM(RTRIM(ic.item_code)) <> ''
            ORDER BY ic.item_code;";

        var items = new List<OnlineStoreItem>();

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DefaultGodown", defaultGodown);
        cmd.Parameters.AddWithValue("@PeriodId", periodId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new OnlineStoreItem
            {
                OlItemId      = reader.GetInt32(0),
                ItemNo        = reader.GetGuid(1),
                ErpId         = reader.GetString(2).Trim(),
                Name          = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                RawStock      = reader.GetDecimal(4),
                SellingPrice  = reader.GetDecimal(5),
                OriginalPrice = reader.GetDecimal(6),
                IsActive      = reader.GetBoolean(7),
            });
        }

        return items;
    }

    /// <summary>
    /// Marks successfully-synced rows in fa_ol_items with the values that were
    /// actually SENT (uploaded_stock / uploaded_mrp / uploaded_rate, updatedon).
    /// One UPDATE per row, single connection; ids come from fa_ol_items.id.
    /// </summary>
    public async Task<int> MarkUploadedAsync(
        IEnumerable<(int OlItemId, decimal SentQty, decimal SentMrp, decimal SentRate)> rows)
    {
        const string sql = @"
            UPDATE dbo.fa_ol_items
            SET uploaded_stock = @Qty,
                uploaded_mrp   = @Mrp,
                uploaded_rate  = @Rate,
                updatedon      = GETDATE()
            WHERE id = @Id;";

        int updated = 0;
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        foreach (var row in rows)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Qty",  row.SentQty);
            cmd.Parameters.AddWithValue("@Mrp",  row.SentMrp);
            cmd.Parameters.AddWithValue("@Rate", row.SentRate);
            cmd.Parameters.AddWithValue("@Id",   row.OlItemId);
            updated += await cmd.ExecuteNonQueryAsync();
        }

        return updated;
    }
}
