namespace PulseLegacy.OnlineStoreSync.Services;

/// <summary>
/// Minimal daily-file logger: Logs/sync-yyyyMMdd.log next to the executable,
/// appended per run, and echoed to the console so manual runs are readable.
/// No external logging framework — this utility must stay a single small exe.
/// </summary>
public sealed class FileLogger
{
    private readonly string _logFilePath;
    private readonly string _logsDir;
    private readonly object _gate = new();

    public FileLogger(string baseDirectory)
    {
        _logsDir = Path.Combine(baseDirectory, "Logs");
        Directory.CreateDirectory(_logsDir);
        _logFilePath = Path.Combine(_logsDir, $"sync-{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogFilePath => _logFilePath;

    /// <summary>
    /// Saves the exact request payload of a run to Logs/request-yyyyMMdd-HHmmss.json,
    /// then keeps only the newest <paramref name="keep"/> such files (default 3).
    /// Purely diagnostic — lets us inspect precisely what was sent (incl. unit_quantity).
    /// </summary>
    public void SaveRequestSnapshot(string json, int keep = 3)
    {
        try
        {
            var path = Path.Combine(_logsDir, $"request-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.WriteAllText(path, json);

            var stale = new DirectoryInfo(_logsDir)
                .GetFiles("request-*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(Math.Max(0, keep));
            foreach (var f in stale)
            {
                try { f.Delete(); } catch { /* best-effort prune */ }
            }

            Info($"Request payload saved: {Path.GetFileName(path)} (keeping newest {keep}).");
        }
        catch (Exception ex)
        {
            Warn($"Could not save request snapshot: {ex.Message}");
        }
    }

    public void Info(string message)  => Write("INFO ", message);
    public void Warn(string message)  => Write("WARN ", message);
    public void Error(string message) => Write("ERROR", message);

    public void Error(string message, Exception ex) =>
        Write("ERROR", $"{message}{Environment.NewLine}{ex}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (_gate)
        {
            Console.WriteLine(line);
            try { File.AppendAllText(_logFilePath, line + Environment.NewLine); }
            catch { /* console output already happened; never crash on log I/O */ }
        }
    }
}
