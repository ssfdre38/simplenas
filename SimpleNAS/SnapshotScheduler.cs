using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SimpleNAS;

public record SnapshotSchedule(
    bool Enabled,
    string Frequency, // "hourly", "daily", "weekly"
    string TargetPool, // e.g. "tank" or "all"
    int RetentionCount, // e.g. 7
    DateTime? LastRun,
    DateTime? NextRun
);

public class SnapshotSchedulerService : BackgroundService
{
    private readonly ILogger<SnapshotSchedulerService> _logger;
    private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "snapshot_schedule.json");
    private static readonly object FileLock = new();

    public SnapshotSchedulerService(ILogger<SnapshotSchedulerService> logger)
    {
        _logger = logger;
    }

    public static SnapshotSchedule GetSchedule()
    {
        lock (FileLock)
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    var json = File.ReadAllText(ConfigPath);
                    var sched = JsonSerializer.Deserialize<SnapshotSchedule>(json);
                    if (sched != null) return sched;
                }
                catch { }
            }

            return new SnapshotSchedule(
                Enabled: false,
                Frequency: "daily",
                TargetPool: "tank",
                RetentionCount: 7,
                LastRun: null,
                NextRun: null
            );
        }
    }

    public static void SaveSchedule(SnapshotSchedule schedule)
    {
        lock (FileLock)
        {
            var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SimpleNAS Automated Snapshot Scheduler started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var schedule = GetSchedule();
                if (schedule.Enabled)
                {
                    var now = DateTime.UtcNow;
                    if (!schedule.NextRun.HasValue || now >= schedule.NextRun.Value)
                    {
                        await RunSnapshotTaskAsync(schedule);

                        var nextRun = schedule.Frequency.ToLowerInvariant() switch
                        {
                            "hourly" => now.AddHours(1),
                            "weekly" => now.AddDays(7),
                            _ => now.AddDays(1) // "daily" default
                        };

                        var updated = schedule with { LastRun = now, NextRun = nextRun };
                        SaveSchedule(updated);
                        _logger.LogInformation("Automated snapshot completed. Next run scheduled for {NextRun}", nextRun);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing automated snapshot cycle");
            }

            // Check every 60 seconds
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }

    public static Task RunSnapshotTaskAsync(SnapshotSchedule schedule)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var snapLabel = $"auto_{timestamp}";

        if (OperatingSystem.IsLinux())
        {
            try
            {
                var poolName = string.IsNullOrWhiteSpace(schedule.TargetPool) || schedule.TargetPool == "all"
                    ? "tank"
                    : schedule.TargetPool;

                // Take snapshot
                var psi = new System.Diagnostics.ProcessStartInfo("zfs", $"snapshot -r {poolName}@{snapLabel}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using (var proc = System.Diagnostics.Process.Start(psi))
                {
                    proc?.WaitForExit(30000);
                }

                // Prune older snapshots
                var listPsi = new System.Diagnostics.ProcessStartInfo("zfs", $"list -t snapshot -H -o name")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var listProc = System.Diagnostics.Process.Start(listPsi);
                if (listProc != null)
                {
                    var output = listProc.StandardOutput.ReadToEnd();
                    listProc.WaitForExit(10000);

                    var autoSnaps = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .Where(s => s.Contains("@auto_"))
                        .OrderBy(s => s)
                        .ToList();

                    var excess = autoSnaps.Count - schedule.RetentionCount;
                    if (excess > 0)
                    {
                        for (int i = 0; i < excess; i++)
                        {
                            var oldSnap = autoSnaps[i];
                            var destroyPsi = new System.Diagnostics.ProcessStartInfo("zfs", $"destroy {oldSnap}")
                            {
                                UseShellExecute = false
                            };
                            using var destroyProc = System.Diagnostics.Process.Start(destroyPsi);
                            destroyProc?.WaitForExit(10000);
                        }
                    }
                }
            }
            catch { }
        }
        else
        {
            // Windows mock & shadow record tracking
            try
            {
                var snapPath = Path.Combine(Directory.GetCurrentDirectory(), "mock_snapshots.txt");
                var poolName = schedule.TargetPool ?? "tank";
                var newEntry = $"{poolName}/media@{snapLabel}\t1M\t-\t450G\t-\n";
                File.AppendAllText(snapPath, newEntry);

                // Prune
                if (File.Exists(snapPath))
                {
                    var lines = File.ReadAllLines(snapPath).ToList();
                    var autoLines = lines.Where(l => l.Contains("@auto_")).ToList();
                    var nonAutoLines = lines.Where(l => !l.Contains("@auto_")).ToList();

                    var excess = autoLines.Count - schedule.RetentionCount;
                    if (excess > 0)
                    {
                        autoLines = autoLines.Skip(excess).ToList();
                    }

                    File.WriteAllLines(snapPath, nonAutoLines.Concat(autoLines));
                }
            }
            catch { }
        }

        return Task.CompletedTask;
    }
}
