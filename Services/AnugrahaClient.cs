using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class AnugrahaClient : IDisposable
{
    public sealed class SendResult
    {
        public bool HttpSuccess { get; set; }
        public int StatusCode { get; set; }
        public string RawBody { get; set; } = string.Empty;
        public AnugrahaProductUpdateResponse? Parsed { get; set; }
        public string? TransportError { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly string _endpoint;

    public AnugrahaClient(string endpoint, string apiKey, int timeoutSeconds)
    {
        _endpoint = endpoint;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<SendResult> SendBatchAsync(AnugrahaProductUpdateRequest request)
    {
        try
        {
            var json = JsonSerializer.Serialize(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(_endpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            AnugrahaProductUpdateResponse? parsed = null;
            try { parsed = JsonSerializer.Deserialize<AnugrahaProductUpdateResponse>(body, JsonOpts); }
            catch { }

            return new SendResult
            {
                HttpSuccess = response.IsSuccessStatusCode,
                StatusCode = (int)response.StatusCode,
                RawBody = body,
                Parsed = parsed
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SendResult
            {
                HttpSuccess = false,
                TransportError = ex.Message
            };
        }
    }

    public void Dispose() => _http.Dispose();
}
