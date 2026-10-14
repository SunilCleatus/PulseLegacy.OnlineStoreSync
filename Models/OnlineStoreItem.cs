namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// One row read from the legacy database:
/// fa_ol_items (selection) ⋈ fa_item_codes (details) ⋈ fa_item_master (stock).
/// </summary>
public sealed class OnlineStoreItem
{
    /// <summary>fa_ol_items.id — used to write back upload state after a successful sync.</summary>
    public int OlItemId { get; set; }

    public Guid ItemNo { get; set; }

    /// <summary>fa_item_codes.item_code -> external ERP/item identifier.</summary>
    public string ErpId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Raw stock for the default godown and configured period:
    /// opening_quantity + receipt_quantity - issue_quantity.
    /// </summary>
    public decimal RawStock { get; set; }

    public decimal SellingPrice { get; set; }

    public decimal OriginalPrice { get; set; }

    public bool IsActive { get; set; }

    public decimal FixedStock { get; set; }

    public decimal FixedRate { get; set; }

    public decimal FixedMrp { get; set; }

    public decimal? AdvancedPrice { get; set; }

    /// <summary>Resolved stock after overrides and negative-stock handling.</summary>
    public decimal ResolvedStock { get; set; }

    /// <summary>Resolved rate after overrides.</summary>
    public decimal ResolvedRate { get; set; }

    /// <summary>Resolved MRP after overrides.</summary>
    public decimal ResolvedMrp { get; set; }

}
