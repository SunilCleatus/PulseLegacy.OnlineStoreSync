using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class DhanyaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AppConfig.DhanyaConfig _config;
    private readonly FileLogger _log;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public DhanyaClient(AppConfig.DhanyaConfig config, FileLogger log)
    {
        _config = config;
        _log = log;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds) };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", config.ApiKey);
        if (!string.IsNullOrWhiteSpace(config.ShopId))
            _httpClient.DefaultRequestHeaders.Add("Shop-Id", config.ShopId);
        _httpClient.DefaultRequestHeaders.Add("Godown-Id", config.GodownId);
    }

   public async Task<string?> SendBatchAsync(List<DhanyaProductDto> products, CancellationToken cancellationToken = default)
{
    if (products.Count == 0)
        return null;

    var requestPayload = new DhanyaInventoryRequest { products = products };
    var json = JsonSerializer.Serialize(requestPayload, _jsonOptions);
    var content = new StringContent(json, Encoding.UTF8, "application/json");

    _log.Info($"Sending batch of {products.Count} products to Dhanya.");

    try
    {
        var response = await _httpClient.PostAsync(_config.Endpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        _log.Info($"Dhanya Raw Response Body: {responseBody}");

        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
        {
            var taskResponse = JsonSerializer.Deserialize<DhanyaTaskResponse>(responseBody, _jsonOptions);
            var taskId = taskResponse?.Data?.TaskId;                          // CHANGED
            _log.Info($"Dhanya batch accepted. Task ID: {taskId}");           // CHANGED
            return taskId;                                                    // CHANGED
        }

        _log.Error($"Dhanya API error: {(int)response.StatusCode} - {responseBody}");
        return null;
    }
    catch (Exception ex)
    {
        _log.Error("Dhanya client exception.", ex);
        return null;
    }
}

    public async Task<DhanyaTaskResponse?> GetTaskStatusAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) return null;

        var statusUrl = BuildTaskStatusUrl(taskId);

        try
        {
            var response = await _httpClient.GetAsync(statusUrl, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return JsonSerializer.Deserialize<DhanyaTaskResponse>(body, _jsonOptions);

            _log.Warn($"Dhanya task status HTTP {(int)response.StatusCode} for {taskId}: {body}");
        }
        catch (Exception ex)
        {
            _log.Error($"Dhanya task status error for {taskId}.", ex);
        }

        return null;
    }

    public async Task<DhanyaTaskResponse?> GetTaskStatusAsync(string taskId)
{
    if (string.IsNullOrEmpty(taskId)) return null;

    string statusUrl = $"{_config.TaskStatusEndpoint.TrimEnd('/')}/{taskId}";

    try
    {
        using var requestMsg = new HttpRequestMessage(HttpMethod.Get, statusUrl);
        if (!string.IsNullOrWhiteSpace(_config.TaskStatusApiKey))
            requestMsg.Headers.Add("X-API-Key", _config.TaskStatusApiKey);
        if (!string.IsNullOrWhiteSpace(_config.ShopId))
            requestMsg.Headers.Add("Shop-Id", _config.ShopId);
        requestMsg.Headers.Add("Godown-Id", _config.GodownId);

        var response = await _httpClient.SendAsync(requestMsg);
        var body = await response.Content.ReadAsStringAsync();

        _log.Info($"Task Status URL: {statusUrl}");
        _log.Info($"Task Status Response Code: {(int)response.StatusCode}");
        _log.Info($"Task Status Raw Body: {body}");

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<DhanyaTaskResponse>(body);
        }
        else
        {
            _log.Error($"Task status call failed: {(int)response.StatusCode} - {body}", new Exception(body));
        }
    }
    catch (Exception ex)
    {
        _log.Error("Get Task Status Exception", ex);
    }

    return null;
}

    private string BuildTaskStatusUrl(string taskId)
    {
        var baseUri = new Uri(_config.Endpoint, UriKind.Absolute);
        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var inventoryIndex = basePath.LastIndexOf("/inventory", StringComparison.OrdinalIgnoreCase);
        var taskPath = inventoryIndex >= 0
            ? basePath[..inventoryIndex] + $"/tasks/{taskId}"
            : basePath + $"/tasks/{taskId}";

        return new UriBuilder(baseUri) { Path = taskPath }.Uri.ToString();
    }

    public void Dispose() => _httpClient.Dispose();
}
