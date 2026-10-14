using System.Text.Json;
using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class AnugrahaStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _stateFilePath;

    public AnugrahaStateStore(string baseDirectory)
    {
        var stateDir = Path.Combine(baseDirectory, "State");
        Directory.CreateDirectory(stateDir);
        _stateFilePath = Path.Combine(stateDir, "anugraha_state.json");
    }

    public async Task<Dictionary<string, AnugrahaItemState>> LoadAsync()
    {
        if (!File.Exists(_stateFilePath))
            return new Dictionary<string, AnugrahaItemState>(StringComparer.OrdinalIgnoreCase);

        await using var stream = File.OpenRead(_stateFilePath);
        var items = await JsonSerializer.DeserializeAsync<List<AnugrahaItemState>>(stream)
            ?? new List<AnugrahaItemState>();

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.ErpId))
            .GroupBy(item => item.ErpId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(Dictionary<string, AnugrahaItemState> state)
    {
        var items = state.Values
            .OrderBy(item => item.ErpId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var stream = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(stream, items, JsonOptions);
    }
}
