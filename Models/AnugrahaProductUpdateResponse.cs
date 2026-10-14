using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

public sealed class AnugrahaProductUpdateResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public ResponseData? Data { get; set; }

    [JsonPropertyName("errors")]
    public ResponseErrors? Errors { get; set; }

    [JsonIgnore]
    public IReadOnlyList<FailedProduct> FailedProducts =>
        (Data?.Failed ?? Errors?.Failed) ?? new List<FailedProduct>();

    public sealed class ResponseData
    {
        [JsonPropertyName("updated_count")]
        public int UpdatedCount { get; set; }

        [JsonPropertyName("failed_count")]
        public int FailedCount { get; set; }

        [JsonPropertyName("failed")]
        public List<FailedProduct>? Failed { get; set; }
    }

    public sealed class ResponseErrors
    {
        [JsonPropertyName("failed")]
        public List<FailedProduct>? Failed { get; set; }
    }

    public sealed class FailedProduct
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
