using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

/// <summary>
/// Thin HTTP client for the Zepio Products Update API (bulk-update endpoint).
/// Bearer-token auth; one POST per batch (max 100 products per Zepio rules).
/// Never throws for API-level failures — returns a typed result the runner can log.
/// </summary>
public sealed class ZepioClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public ZepioClient(string endpoint, string apiKey, int timeoutSeconds)
    {
        _endpoint = endpoint;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public sealed class BatchResult
    {
        public bool   HttpSuccess   { get; init; }
        public int    StatusCode    { get; init; }
        public string RawBody       { get; init; } = string.Empty;
        public ZepioProductUpdateResponse? Parsed { get; init; }
        public string? TransportError { get; init; }
    }

    /// <summary>Sends one batch. Catches transport errors; never throws.</summary>
    public async Task<BatchResult> SendBatchAsync(ZepioProductUpdateRequest request)
    {
        try
        {
            var json    = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _http.PostAsync(_endpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            ZepioProductUpdateResponse? parsed = null;
            try { parsed = JsonSerializer.Deserialize<ZepioProductUpdateResponse>(body, JsonOpts); }
            catch { /* non-JSON body (e.g. HTML error page) — RawBody is logged instead */ }

            return new BatchResult
            {
                HttpSuccess = response.IsSuccessStatusCode,
                StatusCode  = (int)response.StatusCode,
                RawBody     = body,
                Parsed      = parsed,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network failure or timeout — outcome for this batch is failed/unknown.
            return new BatchResult
            {
                HttpSuccess    = false,
                StatusCode     = 0,
                TransportError = ex.Message,
            };
        }
    }

    public void Dispose() => _http.Dispose();
}
