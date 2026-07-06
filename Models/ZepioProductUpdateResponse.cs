using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// Response from the Zepio bulk-update API.
/// Success shape:  { success, message, data: { updated_count, failed_count, failed: [] } }
/// Failure shape:  { success, message, errors: { failed: [ { index, erp_id, branch_id, message } ] } }
/// Both shapes are mapped here; FailedProducts unifies data.failed and errors.failed.
/// </summary>
public sealed class ZepioProductUpdateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public ZepioResponseData? Data { get; set; }

    [JsonPropertyName("errors")]
    public ZepioResponseErrors? Errors { get; set; }

    /// <summary>All failed products regardless of which envelope they arrived in.</summary>
    [JsonIgnore]
    public IReadOnlyList<ZepioFailedProduct> FailedProducts =>
        (Data?.Failed ?? Errors?.Failed) ?? new List<ZepioFailedProduct>();

    public sealed class ZepioResponseData
    {
        [JsonPropertyName("updated_count")]
        public int UpdatedCount { get; set; }

        [JsonPropertyName("failed_count")]
        public int FailedCount { get; set; }

        [JsonPropertyName("failed")]
        public List<ZepioFailedProduct>? Failed { get; set; }
    }

    public sealed class ZepioResponseErrors
    {
        [JsonPropertyName("failed")]
        public List<ZepioFailedProduct>? Failed { get; set; }
    }

    public sealed class ZepioFailedProduct
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("erp_id")]
        public string? ErpId { get; set; }

        [JsonPropertyName("branch_id")]
        public int BranchId { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
