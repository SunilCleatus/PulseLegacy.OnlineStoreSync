using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// One product in the Zepio bulk-update payload.
/// Field names follow the Zepio Products Update API exactly (snake_case).
/// </summary>
public sealed class ZepioProductDto
{
    [JsonPropertyName("erp_id")]
    public string ErpId { get; set; } = string.Empty;

    // "name" is deliberately NOT sent: the customer maintains product names in the
    // online store itself; the sync must not overwrite them. (Zepio updates only
    // the fields present in the payload.)

    [JsonPropertyName("track_quantity")]
    public bool TrackQuantity { get; set; } = true;

    [JsonPropertyName("unit_quantity")]
    public decimal UnitQuantity { get; set; }

    [JsonPropertyName("selling_price")]
    public decimal SellingPrice { get; set; }

    [JsonPropertyName("original_price")]
    public decimal OriginalPrice { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}
