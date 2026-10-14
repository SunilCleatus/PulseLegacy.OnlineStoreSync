using System.Text.Json;
using System.Text.Json.Serialization;

namespace PulseLegacy.OnlineStoreSync.Models;

/// <summary>
/// Strongly-typed view of appsettings.json.
/// </summary>
public sealed class AppConfig
{
    [JsonPropertyName("ActiveSync")]
    public string ActiveSync { get; set; } = string.Empty;

    [JsonPropertyName("ConnectionStrings")]
    public ConnectionStringsSection ConnectionStrings { get; set; } = new();

    [JsonPropertyName("Zepio")]
    public ZepioSection Zepio { get; set; } = new();

    [JsonPropertyName("Sync")]
    public SyncSection Sync { get; set; } = new();

    [JsonPropertyName("Dhanya")]
    public DhanyaConfig Dhanya { get; set; } = new();

    [JsonPropertyName("Anugraha")]
    public AnugrahaConfig Anugraha { get; set; } = new();

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
        [JsonPropertyName("AutoDetectPeriod")]
        public bool AutoDetectPeriod { get; set; } = true;

        [JsonPropertyName("PeriodId")]
        public int PeriodId { get; set; }

        [JsonPropertyName("SendNegativeStockAsZero")]
        public bool SendNegativeStockAsZero { get; set; } = true;

        [JsonPropertyName("UpdateFaOlItemsAfterSuccess")]
        public bool UpdateFaOlItemsAfterSuccess { get; set; } = true;

        [JsonPropertyName("DryRun")]
        public bool DryRun { get; set; }
    }

    public sealed class DhanyaConfig
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; }

        [JsonPropertyName("Endpoint")]
        public string Endpoint { get; set; } = string.Empty;

        [JsonPropertyName("ApiKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("ShopId")]
        public string ShopId { get; set; } = string.Empty;

        [JsonPropertyName("GodownId")]
        public string GodownId { get; set; } = string.Empty;

        [JsonPropertyName("BatchSize")]
        public int BatchSize { get; set; } = 10000;

        [JsonPropertyName("TimeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 60;

        [JsonPropertyName("TaskPollIntervalSeconds")]
        public int TaskPollIntervalSeconds { get; set; } = 10;

        [JsonPropertyName("MaxPollAttempts")]
        public int MaxPollAttempts { get; set; } = 30;

        [JsonPropertyName("TaskStatusEndpoint")]

        public string TaskStatusEndpoint { get; set; } = string.Empty;
        [JsonPropertyName("TaskStatusApiKey")]
        public string TaskStatusApiKey { get; set; } = string.Empty;

        public string MaskedApiKey =>
            string.IsNullOrEmpty(ApiKey) || ApiKey.Length < 4
                ? "****"
                : ApiKey[..2] + new string('*', Math.Max(0, ApiKey.Length - 2));
    }

    public sealed class AnugrahaConfig
    {
        [JsonPropertyName("Enabled")]
        public bool Enabled { get; set; }

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

        public string MaskedApiKey =>
            string.IsNullOrEmpty(ApiKey) || ApiKey.Length < 4
                ? "****"
                : ApiKey[..2] + new string('*', Math.Max(0, ApiKey.Length - 2));
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Configuration file not found: {path}. Place appsettings.json next to the executable.");

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

        if (!Anugraha.Enabled)
        {
            if (string.IsNullOrWhiteSpace(Zepio.Endpoint))
                errors.Add("Zepio:Endpoint is required.");
            if (string.IsNullOrWhiteSpace(Zepio.ApiKey) || Zepio.ApiKey == "PUT_API_KEY_HERE")
                errors.Add("Zepio:ApiKey is not set. Edit appsettings.json and enter the API key.");
            if (Zepio.BatchSize < 1 || Zepio.BatchSize > 10000)
                errors.Add("Zepio:BatchSize must be between 1 and 10000 (Zepio allows max 100 products per request).");
            if (Zepio.TimeoutSeconds < 5)
                errors.Add("Zepio:TimeoutSeconds must be at least 5.");
        }

        if (!Sync.AutoDetectPeriod && Sync.PeriodId <= 0)
            errors.Add("Sync:PeriodId is required when Sync:AutoDetectPeriod is false.");

        if (Sync.DryRun)
            errors.RemoveAll(e => e.StartsWith("Zepio:ApiKey", StringComparison.Ordinal));

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid appsettings.json:\n  - " + string.Join("\n  - ", errors));
    }

    public void ValidateDhanya()
    {
        if (!Dhanya.Enabled || Sync.DryRun)
            return;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Dhanya.Endpoint))
            errors.Add("Dhanya:Endpoint is required when Dhanya is enabled.");
        if (string.IsNullOrWhiteSpace(Dhanya.ApiKey))
            errors.Add("Dhanya:ApiKey is required when Dhanya is enabled.");
        if (string.IsNullOrWhiteSpace(Dhanya.GodownId))
            errors.Add("Dhanya:GodownId is required when Dhanya is enabled.");
        if (Dhanya.BatchSize < 1 || Dhanya.BatchSize > 100)
            errors.Add("Dhanya:BatchSize must be between 1 and 100.");
        if (Dhanya.TimeoutSeconds < 5)
            errors.Add("Dhanya:TimeoutSeconds must be at least 5.");
        if (Dhanya.TaskPollIntervalSeconds < 1)
            errors.Add("Dhanya:TaskPollIntervalSeconds must be at least 1.");
        if (Dhanya.MaxPollAttempts < 1)
            errors.Add("Dhanya:MaxPollAttempts must be at least 1.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid appsettings.json:\n  - " + string.Join("\n  - ", errors));
    }

    public void ValidateAnugraha()
    {
        if (!Anugraha.Enabled || Sync.DryRun)
            return;

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Anugraha.Endpoint))
            errors.Add("Anugraha:Endpoint is required when Anugraha is enabled.");
        if (string.IsNullOrWhiteSpace(Anugraha.ApiKey))
            errors.Add("Anugraha:ApiKey is required when Anugraha is enabled.");
        if (Anugraha.BatchSize < 1 || Anugraha.BatchSize > 100)
            errors.Add("Anugraha:BatchSize must be between 1 and 100.");
        if (Anugraha.TimeoutSeconds < 5)
            errors.Add("Anugraha:TimeoutSeconds must be at least 5.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid appsettings.json:\n  - " + string.Join("\n  - ", errors));
    }

    public string MaskedApiKey =>
        string.IsNullOrEmpty(Zepio.ApiKey) ? "(not set)"
        : Zepio.ApiKey.Length <= 4 ? "****"
        : Zepio.ApiKey[..4] + new string('*', Math.Min(Zepio.ApiKey.Length - 4, 12));
}
