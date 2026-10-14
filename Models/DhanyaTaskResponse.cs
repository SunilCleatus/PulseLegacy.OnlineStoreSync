using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

public class DhanyaTaskResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public DhanyaTaskData? Data { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    // Task status fields (used when polling GET /tasks/{id} later — may differ, we'll confirm)
    [JsonPropertyName("status")]
    public string? status { get; set; }

    [JsonPropertyName("summary")]
    public DhanyaTaskSummary? summary { get; set; }

    [JsonPropertyName("results")]
    public List<DhanyaTaskResult> results { get; set; } = new();
}

public class DhanyaTaskData
{
    [JsonPropertyName("taskId")]
    public string TaskId { get; set; } = string.Empty;

    [JsonPropertyName("received")]
    public int Received { get; set; }
}

public class DhanyaTaskSummary
{
    public int received { get; set; }
    public int updated { get; set; }
    public int skipped { get; set; }
}

public class DhanyaTaskResult
{
    public string erpId { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public string? reason { get; set; }
}