using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

public class DhanyaInventoryRequest
{
    [JsonPropertyName("products")]
    public List<DhanyaProductDto> products { get; set; } = new();
}
