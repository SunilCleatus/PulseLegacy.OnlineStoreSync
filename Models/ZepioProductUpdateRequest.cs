using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>Request body for POST /api/developer/v1/products/bulk-update.</summary>
public sealed class ZepioProductUpdateRequest
{
    [JsonPropertyName("branch_id")]
    public int BranchId { get; set; }

    [JsonPropertyName("products")]
    public List<ZepioProductDto> Products { get; set; } = new();
}
