using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

public class DhanyaProductDto
{
    [JsonPropertyName("sku")]
    [JsonPropertyOrder(0)]
    public string erpId { get; set; } = string.Empty;

    [JsonPropertyName("mrp")]
    [JsonPropertyOrder(3)]
    public decimal MRP { get; set; }

    [JsonPropertyName("price")]
    [JsonPropertyOrder(1)]
    public decimal selling_price { get; set; }

    // TODO: re-enable DiscountSellingPrice when needed
    // public decimal discount_selling_price { get; set; }

    [JsonPropertyName("quantity")]
    [JsonPropertyOrder(2)]
    public int Qty { get; set; }
}
