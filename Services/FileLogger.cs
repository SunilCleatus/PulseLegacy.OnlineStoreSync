namespace PulseLegacy.OnlineStoreSync.Services;

/// <summary>
/// Minimal daily-file logger: Logs/sync-yyyyMMdd.log next to the executable,
/// appended per run, and echoed to the console so manual runs are readable.
/// No external logging framework — this utility must stay a single small exe.
/// </summary>
public sealed class FileLogger
{
    private readonly string _logFilePath;
    private readonly object _gate = new();

    public FileLogger(string baseDirectory)
    {
        var logsDir = Path.Combine(baseDirectory, "Logs");
        Directory.CreateDirectory(logsDir);
        _logFilePath = Path.Combine(logsDir, $"sync-{DateTime.Now:yyyyMMdd}.log");
    }

    public string LogFilePath => _logFilePath;

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
