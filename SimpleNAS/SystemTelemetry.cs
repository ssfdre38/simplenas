using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SimpleNAS;

public static class SystemTelemetry
{
    // ==================== WIN32 P/INVOKE ====================
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public ulong Value => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    // ==================== CPU SAMPLING (WINDOWS & LINUX) ====================
    private static ulong _prevWinIdle;
    private static ulong _prevWinKernel;
    private static ulong _prevWinUser;
    private static DateTime _prevWinSampleTime = DateTime.MinValue;
    private static double _lastWinCpuUsage = 0.0;
    private static readonly object _winCpuLock = new();

    private static ulong _prevLinuxActive;
    private static ulong _prevLinuxTotal;
    private static DateTime _prevLinuxSampleTime = DateTime.MinValue;
    private static double _lastLinuxCpuUsage = 0.0;
    private static readonly object _linuxCpuLock = new();

    private static (ulong Active, ulong Total)? ReadLinuxCpuTimes()
    {
        try
        {
            if (File.Exists("/proc/stat"))
            {
                var line = File.ReadLines("/proc/stat").FirstOrDefault(l => l.StartsWith("cpu "));
                if (line != null)
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5 &&
                        ulong.TryParse(parts[1], out var user) &&
                        ulong.TryParse(parts[2], out var nice) &&
                        ulong.TryParse(parts[3], out var system) &&
                        ulong.TryParse(parts[4], out var idle))
                    {
                        ulong iowait = 0, irq = 0, softirq = 0, steal = 0;
                        if (parts.Length >= 6) ulong.TryParse(parts[5], out iowait);
                        if (parts.Length >= 7) ulong.TryParse(parts[6], out irq);
                        if (parts.Length >= 8) ulong.TryParse(parts[7], out softirq);
                        if (parts.Length >= 9) ulong.TryParse(parts[8], out steal);

                        var total = user + nice + system + idle + iowait + irq + softirq + steal;
                        var active = user + nice + system + irq + softirq + steal;
                        return (active, total);
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static double GetCpuPercent()
    {
        if (OperatingSystem.IsWindows())
        {
            lock (_winCpuLock)
            {
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    var now = DateTime.UtcNow;
                    if (_prevWinSampleTime == DateTime.MinValue)
                    {
                        _prevWinIdle = idle.Value;
                        _prevWinKernel = kernel.Value;
                        _prevWinUser = user.Value;
                        _prevWinSampleTime = now;
                        Thread.Sleep(50);
                        if (GetSystemTimes(out idle, out kernel, out user))
                        {
                            var iDiff = idle.Value - _prevWinIdle;
                            var kDiff = kernel.Value - _prevWinKernel;
                            var uDiff = user.Value - _prevWinUser;
                            var totalDiff = kDiff + uDiff;
                            if (totalDiff > 0)
                            {
                                _lastWinCpuUsage = Math.Clamp((1.0 - ((double)iDiff / totalDiff)) * 100.0, 0.0, 100.0);
                            }
                            _prevWinIdle = idle.Value;
                            _prevWinKernel = kernel.Value;
                            _prevWinUser = user.Value;
                            _prevWinSampleTime = DateTime.UtcNow;
                        }
                        return Math.Round(_lastWinCpuUsage, 1);
                    }

                    var idleDiff = idle.Value - _prevWinIdle;
                    var kernelDiff = kernel.Value - _prevWinKernel;
                    var userDiff = user.Value - _prevWinUser;
                    var total = kernelDiff + userDiff;

                    if (total > 0)
                    {
                        _lastWinCpuUsage = Math.Clamp((1.0 - ((double)idleDiff / total)) * 100.0, 0.0, 100.0);
                    }

                    _prevWinIdle = idle.Value;
                    _prevWinKernel = kernel.Value;
                    _prevWinUser = user.Value;
                    _prevWinSampleTime = now;
                }
                return Math.Round(_lastWinCpuUsage, 1);
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            lock (_linuxCpuLock)
            {
                var sample = ReadLinuxCpuTimes();
                if (sample.HasValue)
                {
                    var (active, total) = sample.Value;
                    if (_prevLinuxSampleTime == DateTime.MinValue)
                    {
                        _prevLinuxActive = active;
                        _prevLinuxTotal = total;
                        _prevLinuxSampleTime = DateTime.UtcNow;
                        Thread.Sleep(50);
                        var sample2 = ReadLinuxCpuTimes();
                        if (sample2.HasValue)
                        {
                            var activeDiff = sample2.Value.Active - _prevLinuxActive;
                            var totalDiff = sample2.Value.Total - _prevLinuxTotal;
                            if (totalDiff > 0)
                            {
                                _lastLinuxCpuUsage = Math.Clamp(((double)activeDiff / totalDiff) * 100.0, 0.0, 100.0);
                            }
                            _prevLinuxActive = sample2.Value.Active;
                            _prevLinuxTotal = sample2.Value.Total;
                            _prevLinuxSampleTime = DateTime.UtcNow;
                        }
                        return Math.Round(_lastLinuxCpuUsage, 1);
                    }

                    var aDiff = active - _prevLinuxActive;
                    var tDiff = total - _prevLinuxTotal;
                    if (tDiff > 0)
                    {
                        _lastLinuxCpuUsage = Math.Clamp(((double)aDiff / tDiff) * 100.0, 0.0, 100.0);
                    }
                    _prevLinuxActive = active;
                    _prevLinuxTotal = total;
                    _prevLinuxSampleTime = DateTime.UtcNow;
                }
                return Math.Round(_lastLinuxCpuUsage, 1);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            return 0.0;
        }

        return 0.0;
    }

    // ==================== MEMORY ====================
    public static double GetMemoryPercent()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem) && mem.ullTotalPhys > 0)
                {
                    var used = mem.ullTotalPhys - mem.ullAvailPhys;
                    return Math.Round(((double)used / mem.ullTotalPhys) * 100.0, 1);
                }
            }
            catch { }
        }
        else if (OperatingSystem.IsLinux())
        {
            try
            {
                if (File.Exists("/proc/meminfo"))
                {
                    ulong total = 0, avail = 0;
                    foreach (var line in File.ReadLines("/proc/meminfo"))
                    {
                        if (line.StartsWith("MemTotal:"))
                        {
                            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) ulong.TryParse(parts[1], out total);
                        }
                        else if (line.StartsWith("MemAvailable:"))
                        {
                            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2) ulong.TryParse(parts[1], out avail);
                        }
                    }
                    if (total > 0)
                    {
                        var used = total - avail;
                        return Math.Round(((double)used / total) * 100.0, 1);
                    }
                }
            }
            catch { }
        }
        else if (OperatingSystem.IsMacOS())
        {
            try
            {
                var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (totalBytes > 0)
                {
                    return Math.Round(((double)Process.GetCurrentProcess().WorkingSet64 / totalBytes) * 100.0, 1);
                }
            }
            catch { }
        }

        return 0.0;
    }

    // ==================== ROOT DISK USAGE ====================
    public static string GetDiskPercent()
    {
        try
        {
            var root = Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? (OperatingSystem.IsWindows() ? "C:\\" : "/");
            var drive = new DriveInfo(root);
            if (drive.IsReady && drive.TotalSize > 0)
            {
                var used = drive.TotalSize - drive.AvailableFreeSpace;
                var pct = (int)Math.Round(((double)used / drive.TotalSize) * 100.0);
                return $"{pct}%";
            }
        }
        catch { }

        return "0%";
    }

    // ==================== DISK FORMATTER ====================
    public record TelemetrySnapshot(
        string Platform,
        string OsDescription,
        double Cpu,
        double Memory,
        string Disk
    );

    public static TelemetrySnapshot GetLiveTelemetry()
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : (OperatingSystem.IsLinux() ? "linux" : "macos");
        var osDesc = RuntimeInformation.OSDescription;
        var cpu = GetCpuPercent();
        var mem = GetMemoryPercent();
        var disk = GetDiskPercent();
        return new TelemetrySnapshot(platform, osDesc, cpu, mem, disk);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) bytes = 0;
        string[] suffixes = ["B", "K", "M", "G", "T", "P"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1}{suffixes[counter]}";
    }

    // ==================== BLOCK DEVICES ====================
    public static object GetBlockDevices(Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var output = runCommand("lsblk", ["-J", "-o", "NAME,SIZE,TYPE,MOUNTPOINT,MODEL"]);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return JsonDocument.Parse(output).RootElement;
                }
            }
            catch { }
        }

        // Live Windows or fallback drives
        var blockdevices = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new
            {
                name = d.Name.TrimEnd('\\'),
                size = FormatBytes(d.TotalSize),
                type = d.DriveType switch
                {
                    DriveType.Fixed => "disk",
                    DriveType.Network => "network",
                    DriveType.Removable => "removable",
                    DriveType.CDRom => "rom",
                    _ => "drive"
                },
                mountpoint = d.RootDirectory.FullName,
                model = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.DriveFormat : $"{d.VolumeLabel} ({d.DriveFormat})"
            })
            .ToList();

        return new { blockdevices };
    }

    // ==================== RAW DEVICES FOR ZFS / STORAGE ====================
    public static List<object> GetRawDevices(Func<string, string[], string> runCommand, string fallbackFilePath)
    {
        var devices = new List<object>();
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var output = runCommand("lsblk", ["-d", "-n", "-o", "NAME,SIZE,TYPE"]);
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length >= 3 && fields[2].Trim() == "disk")
                    {
                        devices.Add(new { name = "/dev/" + fields[0].Trim(), size = fields[1].Trim() });
                    }
                }
                if (devices.Count > 0) return devices;
            }
            catch { }
        }
        else
        {
            // On Windows / macOS, report real secondary fixed drives (e.g. D:, G:)
            try
            {
                var secondaryDrives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase) && d.Name != "/")
                    .Select(d => (object)new { name = d.Name.TrimEnd('\\'), size = FormatBytes(d.TotalSize) })
                    .ToList();

                if (secondaryDrives.Count > 0)
                {
                    return secondaryDrives;
                }
            }
            catch { }
        }

        // Fallback to mock file if no secondary drives found
        try
        {
            if (!File.Exists(fallbackFilePath))
            {
                File.WriteAllText(fallbackFilePath, "sdb\t2.0T\tdisk\nsdc\t2.0T\tdisk\nsdd\t2.0T\tdisk\n");
            }
            var output = File.ReadAllText(fallbackFilePath);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 3 && fields[2].Trim() == "disk")
                {
                    devices.Add(new { name = "/dev/" + fields[0].Trim(), size = fields[1].Trim() });
                }
            }
        }
        catch { }

        return devices;
    }

    // ==================== SYSTEM SERVICES ====================
    private static readonly Dictionary<string, string> WindowsServiceMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "smbd", "LanmanServer" },
        { "nmbd", "LanmanWorkstation" },
        { "ssh", "sshd" },
        { "tailscaled", "Tailscale" }
    };

    public static Dictionary<string, string> GetServicesStatus(Func<string, string[], string> runCommand)
    {
        var services = new[] { "smbd", "nmbd", "ssh", "tailscaled" };
        var status = new Dictionary<string, string>();

        foreach (var svc in services)
        {
            status[svc] = GetSingleServiceStatus(svc, runCommand);
        }

        return status;
    }

    private static string GetSingleServiceStatus(string service, Func<string, string[], string> runCommand)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var winName = WindowsServiceMap.TryGetValue(service, out var mapped) ? mapped : service;
                var output = runCommand("sc.exe", ["query", winName]);
                if (output.Contains("STATE") && output.Contains("RUNNING"))
                {
                    return "active";
                }
                return "inactive";
            }
            else
            {
                var isActive = runCommand("systemctl", ["is-active", service]).Trim();
                if (isActive == "active") return "active";

                // Fallback: check process table if systemctl isn't present or systemd is not PID 1
                try
                {
                    var pName = service switch
                    {
                        "smbd" => "smbd",
                        "nmbd" => "nmbd",
                        "tailscaled" => "tailscaled",
                        "ssh" => "sshd",
                        _ => service
                    };
                    if (Process.GetProcessesByName(pName).Length > 0)
                    {
                        return "active";
                    }
                }
                catch { }

                return "inactive";
            }
        }
        catch
        {
            return "inactive";
        }
    }

    public static void ControlService(string service, string action, Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            var winName = WindowsServiceMap.TryGetValue(service, out var mapped) ? mapped : service;
            switch (action.ToLowerInvariant())
            {
                case "start":
                    runCommand("net.exe", ["start", winName]);
                    break;
                case "stop":
                    runCommand("net.exe", ["stop", winName, "/y"]);
                    break;
                case "restart":
                    runCommand("net.exe", ["stop", winName, "/y"]);
                    Thread.Sleep(500);
                    runCommand("net.exe", ["start", winName]);
                    break;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            runCommand("systemctl", [action, service]);
        }
    }

    // ==================== TAILSCALE ====================
    public static (bool Installed, bool Running) GetTailscaleStatus(Func<string, string[], string> runCommand)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = runCommand("sc.exe", ["query", "Tailscale"]);
                var installed = !output.Contains("1060"); // 1060: service does not exist
                var running = output.Contains("STATE") && output.Contains("RUNNING");
                return (installed, running);
            }
            else
            {
                var tailscalePath = runCommand("which", ["tailscale"]).Trim();
                var installed = !string.IsNullOrEmpty(tailscalePath) ||
                                File.Exists("/usr/bin/tailscale") ||
                                File.Exists("/usr/sbin/tailscale") ||
                                File.Exists("/usr/local/bin/tailscale");
                var running = false;
                if (installed)
                {
                    var isActive = runCommand("systemctl", ["is-active", "tailscaled"]).Trim();
                    running = isActive == "active" || Process.GetProcessesByName("tailscaled").Length > 0;
                }
                return (installed, running);
            }
        }
        catch
        {
            return (false, false);
        }
    }

    public static void SetTailscale(bool start, Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            runCommand("net.exe", [start ? "start" : "stop", "Tailscale"]);
        }
        else
        {
            runCommand("systemctl", [start ? "start" : "stop", "tailscaled"]);
        }
    }

    // ==================== FIREWALL ====================
    public static bool GetFirewallActive(Func<string, string[], string> runCommand)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var output = runCommand("netsh.exe", ["advfirewall", "show", "currentprofile"]);
                return output.Contains("State") && output.Contains("ON");
            }
            else
            {
                var output = runCommand("ufw", ["status"]).Trim();
                if (output.Contains("Status: active")) return true;
                if (output.Contains("Status: inactive")) return false;

                try
                {
                    var iptables = runCommand("iptables", ["-L", "-n"]);
                    if (!string.IsNullOrWhiteSpace(iptables) && iptables.Contains("Chain"))
                    {
                        return true;
                    }
                }
                catch { }

                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    public static void SetFirewallActive(bool enable, Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            runCommand("netsh.exe", ["advfirewall", "set", "currentprofile", "state", enable ? "on" : "off"]);
        }
        else if (OperatingSystem.IsLinux())
        {
            if (enable)
            {
                runCommand("ufw", ["--force", "enable"]);
            }
            else
            {
                runCommand("ufw", ["disable"]);
            }
        }
    }

    // ==================== SMB & WINDOWS SHARES ====================
    public static List<SmbShare> GetSmbShares(Func<string, string[], string> runCommand, string smbConfPath, Func<string, List<SmbShare>> parseSmbConf)
    {
        var shares = new List<SmbShare>();
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var output = runCommand("net.exe", ["share"]);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Split('\n');
                    bool started = false;
                    foreach (var rawLine in lines)
                    {
                        var line = rawLine.Trim();
                        if (line.StartsWith("---"))
                        {
                            started = true;
                            continue;
                        }
                        if (!started || string.IsNullOrWhiteSpace(line)) continue;
                        if (line.StartsWith("The command completed", StringComparison.OrdinalIgnoreCase)) break;

                        var parts = System.Text.RegularExpressions.Regex.Split(line, @"\s{2,}");
                        if (parts.Length >= 1)
                        {
                            var name = parts[0].Trim();
                            string path = "";
                            string remark = "";

                            if (parts.Length >= 2)
                            {
                                if (parts[1].Contains(':') || parts[1].Contains('\\') || parts[1].Contains('/'))
                                {
                                    path = parts[1].Trim();
                                    if (parts.Length > 2) remark = string.Join(" ", parts.Skip(2)).Trim();
                                }
                                else
                                {
                                    remark = string.Join(" ", parts.Skip(1)).Trim();
                                }
                            }

                            shares.Add(new SmbShare(name, new Dictionary<string, string>
                            {
                                { "path", path },
                                { "comment", remark },
                                { "read only", "no" },
                                { "guest ok", "yes" }
                            }));
                        }
                    }
                }
            }
            catch { }

            // Merge with local smb.conf shares
            if (File.Exists(smbConfPath))
            {
                try
                {
                    var confShares = parseSmbConf(smbConfPath);
                    foreach (var cs in confShares)
                    {
                        if (!shares.Any(s => string.Equals(s.Name, cs.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            shares.Add(cs);
                        }
                    }
                }
                catch { }
            }

            return shares;
        }
        else
        {
            return parseSmbConf(smbConfPath);
        }
    }

    public static void AddShare(string name, string path, bool readOnly, Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                var perm = readOnly ? "READ" : "FULL";
                runCommand("net.exe", ["share", $"{name}={path}", $"/GRANT:Everyone,{perm}"]);
            }
            catch { }
        }
    }

    public static void DeleteShare(string name, Func<string, string[], string> runCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                runCommand("net.exe", ["share", name, "/delete", "/y"]);
            }
            catch { }
        }
    }

    // ==================== STORAGE POOLS & DATASETS ====================
    public static List<object> GetStoragePools(Func<string, string[], string> runCommand, string poolsFilePath)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var output = runCommand("zpool", ["list", "-H"]);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        .Where(fields => fields.Length >= 10)
                        .Select(fields => (object)new
                        {
                            name = fields[0],
                            size = fields[1],
                            alloc = fields[2],
                            allocated = fields[2],
                            free = fields[3],
                            capacity = fields.Length >= 8 ? fields[7] : fields[6],
                            health = fields[9]
                        })
                        .ToList();
                }
            }
            catch { }
            return [];
        }
        else if (OperatingSystem.IsWindows())
        {
            var pools = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                .Select(d =>
                {
                    var used = d.TotalSize - d.AvailableFreeSpace;
                    var cap = (int)Math.Round(((double)used / d.TotalSize) * 100.0);
                    var driveLetter = d.Name.TrimEnd('\\', ':');
                    var friendlyName = string.IsNullOrWhiteSpace(d.VolumeLabel)
                        ? $"Drive_{driveLetter}"
                        : $"{d.VolumeLabel.Trim().Replace(" ", "_")}_{driveLetter}";

                    return (object)new
                    {
                        name = friendlyName,
                        size = FormatBytes(d.TotalSize),
                        alloc = FormatBytes(used),
                        allocated = FormatBytes(used),
                        free = FormatBytes(d.AvailableFreeSpace),
                        capacity = $"{cap}%",
                        health = "ONLINE"
                    };
                })
                .ToList();

            if (File.Exists(poolsFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(poolsFilePath);
                    foreach (var line in lines)
                    {
                        var fields = line.Split('\t');
                        if (fields.Length >= 10)
                        {
                            var poolName = fields[0];
                            if (pools.Count > 0 && poolName == "tank" && fields[1] == "2.0T")
                                continue;
                            if (!pools.Any(p => ((dynamic)p).name == poolName))
                            {
                                pools.Add(new
                                {
                                    name = fields[0],
                                    size = fields[1],
                                    alloc = fields[2],
                                    allocated = fields[2],
                                    free = fields[3],
                                    capacity = fields.Length >= 8 ? fields[7] : fields[6],
                                    health = fields[9]
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            return pools;
        }

        return [];
    }

    public static List<object> GetStorageDatasets(Func<string, string[], string> runCommand, string datasetsFilePath)
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                var output = runCommand("zfs", ["list", "-H"]);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
                        .Where(fields => fields.Length >= 4)
                        .Select(fields => (object)new
                        {
                            name = fields[0],
                            used = fields[1],
                            avail = fields[2],
                            mountpoint = fields[3]
                        })
                        .ToList();
                }
            }
            catch { }
            return [];
        }
        else if (OperatingSystem.IsWindows())
        {
            var datasets = new List<object>();

            try
            {
                var secondaryDrives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && !d.Name.StartsWith("C:", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var drive in secondaryDrives)
                {
                    var driveLetter = drive.Name.TrimEnd('\\', ':');
                    var di = new DirectoryInfo(drive.RootDirectory.FullName);
                    foreach (var dir in di.GetDirectories())
                    {
                        if (dir.Attributes.HasFlag(FileAttributes.Hidden) || dir.Attributes.HasFlag(FileAttributes.System))
                            continue;

                        datasets.Add(new
                        {
                            name = $"Drive_{driveLetter}/{dir.Name}",
                            used = "<1G",
                            avail = FormatBytes(drive.AvailableFreeSpace),
                            mountpoint = dir.FullName
                        });
                    }
                }
            }
            catch { }

            if (File.Exists(datasetsFilePath))
            {
                try
                {
                    var lines = File.ReadAllLines(datasetsFilePath);
                    foreach (var line in lines)
                    {
                        var fields = line.Split('\t');
                        if (fields.Length >= 4)
                        {
                            var dsName = fields[0];
                            if (datasets.Count > 0 && dsName.StartsWith("tank"))
                                continue;
                            if (!datasets.Any(d => ((dynamic)d).name == dsName))
                            {
                                datasets.Add(new
                                {
                                    name = fields[0],
                                    used = fields[1],
                                    avail = fields[2],
                                    mountpoint = fields[3]
                                });
                            }
                        }
                    }
                }
                catch { }
            }

            if (datasets.Count == 0)
            {
                datasets.Add(new
                {
                    name = "Drive_D/Media",
                    used = "0B",
                    avail = "3.6T",
                    mountpoint = "D:\\Media"
                });
                datasets.Add(new
                {
                    name = "Drive_D/Backups",
                    used = "0B",
                    avail = "3.6T",
                    mountpoint = "D:\\Backups"
                });
            }

            return datasets;
        }

        return [];
    }
}

public record SmbShare(string Name, Dictionary<string, string> Config);
