using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

public sealed class AnugrahaProductUpdateRequest
{
    [JsonPropertyName("branch_id")]
    public int BranchId { get; set; }

    [JsonPropertyName("products")]
    public List<AnugrahaProductDto> Products { get; set; } = new();
}
