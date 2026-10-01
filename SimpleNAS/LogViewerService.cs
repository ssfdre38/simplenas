using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace SimpleNAS;

public record AuditLogEntry(
    string Id,
    DateTime Timestamp,
    string Level, // "Info", "Warning", "Error", "Security"
    string Category, // "Auth", "Storage", "Share", "Network", "Plugin", "System"
    string User,
    string Message,
    string? Details = null
);

public static class LogViewerService
{
    private static readonly ConcurrentQueue<AuditLogEntry> _auditLogs = new();
    private static readonly string LogsDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
    private static readonly string AuditLogFile = Path.Combine(LogsDir, "audit.log");
    private static readonly object _fileLock = new();

    static LogViewerService()
    {
        try
        {
            if (!Directory.Exists(LogsDir)) Directory.CreateDirectory(LogsDir);

            // Load recent logs from disk if available
            if (File.Exists(AuditLogFile))
            {
                var lines = File.ReadLines(AuditLogFile).TakeLast(500);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<AuditLogEntry>(line);
                        if (entry != null) _auditLogs.Enqueue(entry);
                    }
                    catch { }
                }
            }
        }
        catch { }

        // Seed initial startup record if empty
        if (_auditLogs.IsEmpty)
        {
            Record("Info", "System", "SYSTEM", "SimpleNAS Core Service initialized.", "High-performance .NET 10 unified NAS engine online.");
        }
    }

    public static void Record(string level, string category, string user, string message, string? details = null)
    {
        var entry = new AuditLogEntry(
            Id: Guid.NewGuid().ToString("N")[..8],
            Timestamp: DateTime.UtcNow,
            Level: level,
            Category: category,
            User: string.IsNullOrWhiteSpace(user) ? "system" : user,
            Message: message,
            Details: details
        );

        _auditLogs.Enqueue(entry);

        // Keep maximum 1,000 entries in memory ring buffer
        while (_auditLogs.Count > 1000)
        {
            _auditLogs.TryDequeue(out _);
        }

        // Write append to disk
        Task.Run(() =>
        {
            lock (_fileLock)
            {
                try
                {
                    if (!Directory.Exists(LogsDir)) Directory.CreateDirectory(LogsDir);
                    var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
                    File.AppendAllText(AuditLogFile, line);
                }
                catch { }
            }
        });
    }

    public static List<AuditLogEntry> GetAuditLogs(string? level = null, string? category = null, string? search = null, int limit = 150)
    {
        var query = _auditLogs.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(level) && !level.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Level.Equals(level, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category) && !category.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(l => l.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l => 
                l.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                l.User.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (l.Details != null && l.Details.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        return query.OrderByDescending(l => l.Timestamp).Take(limit).ToList();
    }

    public static string GetSystemJournal(int lines = 80)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"Get-WinEvent -LogName System,Application -MaxEvents {lines} -ErrorAction SilentlyContinue | Select-Object TimeCreated, LevelDisplayName, ProviderName, Message | Format-Table -AutoSize | Out-String -Width 160\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    return string.IsNullOrWhiteSpace(output) ? "No recent system events captured." : output;
                }
            }
            else
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "journalctl",
                    Arguments = $"-n {lines} --no-pager -o short-iso",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(3000);
                    return string.IsNullOrWhiteSpace(output) ? "No systemd journal entries captured." : output;
                }
            }
        }
        catch (Exception ex)
        {
            return $"Error retrieving system logs: {ex.Message}";
        }

        return "Log engine standby.";
    }
}
