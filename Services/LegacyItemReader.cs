using Microsoft.Data.SqlClient;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class LegacyItemReader
{
    private readonly string _connectionString;

    public LegacyItemReader(string connectionString)
        => _connectionString = connectionString;

    public async Task<int?> GetCurrentPeriodIdAsync()
    {
        const string sql = @"
            DECLARE @Now datetime = GETDATE();

            SELECT TOP 1 period_id
            FROM dbo.fa_periods
            WHERE @Now >= period_from AND @Now < DATEADD(DAY, 1, period_to)
            ORDER BY CASE WHEN period_status = 'Open' THEN 0 ELSE 1 END, period_from DESC;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        await using (var cmd = new SqlCommand(sql, conn))
        {
            var byDate = await cmd.ExecuteScalarAsync();
            if (byDate != null && byDate != DBNull.Value)
                return Convert.ToInt32(byDate);
        }

        const string openSql = @"
            SELECT TOP 1 period_id
            FROM dbo.fa_periods
            WHERE period_status = 'Open'
            ORDER BY period_from DESC;";

        await using var openCmd = new SqlCommand(openSql, conn);
        var byStatus = await openCmd.ExecuteScalarAsync();
        return (byStatus != null && byStatus != DBNull.Value) ? Convert.ToInt32(byStatus) : (int?)null;
    }

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
                ic.retail_rate                         AS selling_price,
                ic.mrp                                  AS original_price,
                ic.active                               AS is_active,
                ISNULL(oi.fixed_stock, 0)               AS fixed_stock,
                ISNULL(oi.fixed_rate,  0)               AS fixed_rate,
                ISNULL(oi.fixed_mrp,   0)               AS fixed_mrp
            FROM dbo.fa_ol_items oi
            INNER JOIN dbo.fa_item_codes ic
                ON ic.item_code = oi.item_code
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
                OlItemId = reader.GetInt32(0),
                ItemNo = reader.GetGuid(1),
                ErpId = reader.GetString(2).Trim(),
                Name = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                RawStock = reader.GetDecimal(4),
                SellingPrice = reader.GetDecimal(5),
                OriginalPrice = reader.GetDecimal(6),
                IsActive = reader.GetBoolean(7),
                FixedStock = reader.GetDecimal(8),
                FixedRate = reader.GetDecimal(9),
                FixedMrp = reader.GetDecimal(10),
            });
        }

        return items;
    }

    public async Task<List<OnlineStoreItem>> ReadItemsForAnugrahaAsync(Guid defaultGodown, int periodId)
    {
        const string sql = """
;WITH sch AS (
    SELECT
        s.item_no,
        CASE
            WHEN s.min_amount > 0 OR s.max_amount > 0 THEN 0
            WHEN s.retail_rate2 > 0 THEN i.MRP - s.retail_rate2
            WHEN s.SchemeRate > 0 THEN s.SchemeRate
            WHEN s.retail_rate3 > 0 THEN i.MRP - ((i.MRP * s.retail_rate3) / 100.0)
            ELSE NULL
        END AS AdvancedPrice,
        ROW_NUMBER() OVER(PARTITION BY s.item_no ORDER BY s.createddate DESC, s.vid DESC) rn
    FROM dbo.fa_scheme_1 s
    JOIN dbo.fa_item_codes i ON i.item_no = s.item_no
    WHERE s.Active = 1
      AND CONVERT(date,GETDATE()) BETWEEN CONVERT(date,s.from_date) AND CONVERT(date,s.to_date)
)
SELECT
    oi.id AS ol_item_id,
    ic.item_no,
    ic.item_code AS erp_id,
    ic.item_name AS name,
    ISNULL(im.opening_quantity,0)+ISNULL(im.receipt_quantity,0)-ISNULL(im.issue_quantity,0) AS raw_stock,
    ic.retail_rate1 AS selling_price,
    ic.mrp AS original_price,
    ic.active AS is_active,
    ISNULL(oi.fixed_stock,0) AS fixed_stock,
    ISNULL(oi.fixed_rate,0) AS fixed_rate,
    ISNULL(oi.fixed_mrp,0) AS fixed_mrp,
    sch.AdvancedPrice
FROM dbo.fa_ol_items oi
JOIN dbo.fa_item_codes ic ON ic.item_code=oi.item_code
LEFT JOIN dbo.fa_item_master im
    ON im.item_no=ic.item_no
   AND im.godown_code=@DefaultGodown
   AND im.period_id=@PeriodId
LEFT JOIN sch
    ON sch.item_no=ic.item_no
   AND sch.rn=1
WHERE ic.item_code IS NOT NULL
  AND LTRIM(RTRIM(ic.item_code))<>''
ORDER BY ic.item_code;
""";

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
                OlItemId = reader.GetInt32(0),
                ItemNo = reader.GetGuid(1),
                ErpId = reader.GetString(2).Trim(),
                Name = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim(),
                RawStock = reader.GetDecimal(4),
                SellingPrice = reader.GetDecimal(5),
                OriginalPrice = reader.GetDecimal(6),
                IsActive = reader.GetBoolean(7),
                FixedStock = reader.GetDecimal(8),
                FixedRate = reader.GetDecimal(9),
                FixedMrp = reader.GetDecimal(10),
                AdvancedPrice = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
            });
        }

        return items;
    }

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
            cmd.Parameters.AddWithValue("@Qty", row.SentQty);
            cmd.Parameters.AddWithValue("@Mrp", row.SentMrp);
            cmd.Parameters.AddWithValue("@Rate", row.SentRate);
            cmd.Parameters.AddWithValue("@Id", row.OlItemId);
            updated += await cmd.ExecuteNonQueryAsync();
        }

        return updated;
    }
}
