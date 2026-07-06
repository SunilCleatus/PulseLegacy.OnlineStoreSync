using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// Strongly-typed view of appsettings.json.
/// Loaded with plain System.Text.Json — no configuration framework needed,
/// which keeps the deployed utility a single small exe + one json file.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("ConnectionStrings")]
    public ConnectionStringsSection ConnectionStrings { get; set; } = new();

    [JsonPropertyName("Zepio")]
    public ZepioSection Zepio { get; set; } = new();

    [JsonPropertyName("Sync")]
    public SyncSection Sync { get; set; } = new();

    public sealed class ConnectionStringsSection
    {
        [JsonPropertyName("LegacyDb")]
        public string LegacyDb { get; set; } = string.Empty;
    }

    public sealed class ZepioSection
    {
        [JsonPropertyName("Endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("BranchId")]
        public int BranchId { get; set; } = 1;

        [JsonPropertyName("BatchSize")]
        public int BatchSize { get; set; } = 100;

        [JsonPropertyName("TimeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 60;
    }

    public sealed class SyncSection
    {
        [JsonPropertyName("PeriodId")]
        public int PeriodId { get; set; }

        [JsonPropertyName("SendNegativeStockAsZero")]
        public bool SendNegativeStockAsZero { get; set; } = true;

        [JsonPropertyName("UpdateFaOlItemsAfterSuccess")]
        public bool UpdateFaOlItemsAfterSuccess { get; set; } = true;

        [JsonPropertyName("DryRun")]
        public bool DryRun { get; set; }
    }

    // ── Loading + validation ──────────────────────────────────────────────────

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Configuration file not found: {path}. " +
                "Place appsettings.json next to the executable.");

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<AppConfig>(json)
            ?? throw new InvalidOperationException("appsettings.json parsed to null.");

        config.Validate();
        return config;
    }

    private void Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(ConnectionStrings.LegacyDb))
            errors.Add("ConnectionStrings:LegacyDb is required.");
        if (string.IsNullOrWhiteSpace(Zepio.Endpoint))
            errors.Add("Zepio:Endpoint is required.");
        if (string.IsNullOrWhiteSpace(Zepio.ApiKey) || Zepio.ApiKey == "PUT_API_KEY_HERE")
            errors.Add("Zepio:ApiKey is not set. Edit appsettings.json and enter the API key.");
        if (Zepio.BatchSize < 1 || Zepio.BatchSize > 100)
            errors.Add("Zepio:BatchSize must be between 1 and 100 (Zepio allows max 100 products per request).");
        if (Zepio.TimeoutSeconds < 5)
            errors.Add("Zepio:TimeoutSeconds must be at least 5.");
        if (Sync.PeriodId <= 0)
            errors.Add("Sync:PeriodId is required (e.g. 202627).");

        // In DryRun mode a placeholder API key is acceptable — no API call is made.
        if (Sync.DryRun)
            errors.RemoveAll(e => e.StartsWith("Zepio:ApiKey"));

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid appsettings.json:\n  - " + string.Join("\n  - ", errors));
    }

    /// <summary>First 4 characters of the API key, rest masked — safe for logs.</summary>
    public string MaskedApiKey =>
        string.IsNullOrEmpty(Zepio.ApiKey) ? "(not set)"
        : Zepio.ApiKey.Length <= 4 ? "****"
        : Zepio.ApiKey[..4] + new string('*', Math.Min(Zepio.ApiKey.Length - 4, 12));
}
