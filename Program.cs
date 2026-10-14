using PulseLegacy.OnlineStoreSync.Models;
using PulseLegacy.OnlineStoreSync.Services;

// Standalone console utility entry point.
// When Anugraha is enabled in config, run that flow; otherwise keep the
// original Zepio sync flow unchanged.

var baseDir = AppContext.BaseDirectory;
var logger = new FileLogger(baseDir);

try
{
    var config = AppConfig.Load(Path.Combine(baseDir, "appsettings.json"));
    var reader = new LegacyItemReader(config.ConnectionStrings.LegacyDb);
    var stateStore = new AnugrahaStateStore(AppContext.BaseDirectory);

    string activeSync = (config.ActiveSync ?? "").ToLower().Trim();

    switch (activeSync)
    {
        case "zepio":
            {
                Console.WriteLine("=== Starting Zepio Sync ===");
                await new SyncRunner(config, logger).RunAsync();
                break;
            }

        case "anugraha":
            {
                Console.WriteLine("=== Starting Anugraha Sync ===");
                var anugrahaRunner = new AnugrahaSyncRunner(config, reader, logger, stateStore);
                await anugrahaRunner.RunAsync();
                break;
            }

        case "dhanya":
            {
                Console.WriteLine("=== Starting Dhanya Sync ===");
                using var client = new DhanyaClient(config.Dhanya, logger);
                var dhanyaRunner = new DhanyaSyncRunner(config, reader, client, logger);
                await dhanyaRunner.RunAsync();
                break;
            }

        default:
            {
                Console.WriteLine($"ERROR: Invalid ActiveSync '{config.ActiveSync}'.");
                Console.WriteLine("Please set ActiveSync to 'Zepio', 'Anugraha', or 'Dhanya' in appsettings.json");
                return 1;
            }
    }

    Console.WriteLine("Execution completed successfully.");
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine("FATAL ERROR: " + ex.Message);
    return 1;
}