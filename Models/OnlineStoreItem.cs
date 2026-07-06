namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// One row read from the legacy database:
/// fa_ol_items (selection) ⋈ fa_item_codes (details) ⋈ fa_item_master (stock).
/// </summary>
public sealed class OnlineStoreItem
{
    /// <summary>fa_ol_items.id — used to write back uploaded_* after a successful sync.</summary>
    public int OlItemId { get; set; }

    public Guid ItemNo { get; set; }

    /// <summary>fa_item_codes.item_code → Zepio erp_id.</summary>
    public string ErpId { get; set; } = string.Empty;

    /// <summary>fa_item_codes.item_name → Zepio name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Raw stock for the default godown and configured period:
    /// opening_quantity + receipt_quantity - issue_quantity.
    /// May be negative; clamping to zero is applied per configuration.
    /// </summary>
    public decimal RawStock { get; set; }

    /// <summary>fa_item_codes.retail_rate → Zepio selling_price.</summary>
    public decimal SellingPrice { get; set; }

    /// <summary>fa_item_codes.mrp → Zepio original_price.</summary>
    public decimal OriginalPrice { get; set; }

    /// <summary>fa_item_codes.active → Zepio is_active.</summary>
    public bool IsActive { get; set; }
}
