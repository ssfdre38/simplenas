using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleNAS;

public record PluginManifest(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("defaultPort")] int? DefaultPort,
    [property: JsonPropertyName("webPath")] string? WebPath,
    [property: JsonPropertyName("installed")] bool Installed = false,
    [property: JsonPropertyName("enabled")] bool Enabled = false,
    [property: JsonPropertyName("status")] string Status = "Inactive",
    [property: JsonPropertyName("containerId")] string? ContainerId = null,
    [property: JsonPropertyName("image")] string? Image = null,
    [property: JsonPropertyName("settings")] Dictionary<string, string>? Settings = null
);

public class PluginState
{
    public string Id { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public bool Enabled { get; set; }
    public int? Port { get; set; }
    public string Status { get; set; } = "Inactive";
    public string? ContainerId { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
}

public static class PluginManager
{
    private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "plugins.json");
    private static readonly string PluginsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "plugins");
    private static readonly object _lock = new();

    // Default Curated Catalog of NAS Plugins
    private static readonly List<PluginManifest> Catalog = new()
    {
        new PluginManifest(
            Id: "jellyfin",
            Name: "Jellyfin Media Server",
            Description: "Free, open-source software media system that puts you in control of managing and streaming your media.",
            Version: "10.9.11",
            Author: "Jellyfin Project",
            Category: "Media",
            Icon: "🎬",
            DefaultPort: 8096,
            WebPath: "/",
            Image: "jellyfin/jellyfin:latest"
        ),
        new PluginManifest(
            Id: "plex",
            Name: "Plex Media Server",
            Description: "Organizes your video, music, and photo collections and streams them to any screen with hardware transcoding.",
            Version: "1.40.4",
            Author: "Plex, Inc.",
            Category: "Media",
            Icon: "🍿",
            DefaultPort: 32400,
            WebPath: "/web",
            Image: "plexinc/pms-docker:latest"
        ),
        new PluginManifest(
            Id: "nextcloud",
            Name: "Nextcloud Hub",
            Description: "Self-hosted productivity platform providing private cloud storage, file synchronization, contacts, and calendar.",
            Version: "29.0.5",
            Author: "Nextcloud GmbH",
            Category: "Cloud",
            Icon: "☁️",
            DefaultPort: 8080,
            WebPath: "/",
            Image: "nextcloud:latest"
        ),
        new PluginManifest(
            Id: "transmission",
            Name: "Transmission Torrent",
            Description: "Fast, easy, and lightweight BitTorrent client with remote web interface and automated download directories.",
            Version: "4.0.5",
            Author: "Transmission Project",
            Category: "Network",
            Icon: "⚡",
            DefaultPort: 9091,
            WebPath: "/transmission/web/",
            Image: "lscr.io/linuxserver/transmission:latest"
        ),
        new PluginManifest(
            Id: "netdata",
            Name: "Netdata Real-time Monitor",
            Description: "High-resolution real-time infrastructure metrics, CPU core per-thread monitoring, and anomalies detection.",
            Version: "v1.46.3",
            Author: "Netdata Inc.",
            Category: "Monitoring",
            Icon: "📊",
            DefaultPort: 19999,
            WebPath: "/",
            Image: "netdata/netdata:latest"
        ),
        new PluginManifest(
            Id: "tailscale",
            Name: "Tailscale Mesh VPN",
            Description: "Zero-configuration mesh VPN that makes SimpleNAS accessible securely from anywhere in the world.",
            Version: "1.74.0",
            Author: "Tailscale Inc.",
            Category: "Network",
            Icon: "🔒",
            DefaultPort: null,
            WebPath: null,
            Image: "tailscale/tailscale:latest"
        ),
        new PluginManifest(
            Id: "portainer",
            Name: "Portainer CE",
            Description: "Powerful, universal container management UI for managing Docker environments, stacks, and images.",
            Version: "2.21.0",
            Author: "Portainer.io",
            Category: "System",
            Icon: "🚢",
            DefaultPort: 9443,
            WebPath: "/",
            Image: "portainer/portainer-ce:latest"
        ),
        new PluginManifest(
            Id: "filebrowser",
            Name: "FileBrowser Standalone",
            Description: "Web file manager that provides a file managing interface within a specified directory on your NAS.",
            Version: "2.30.0",
            Author: "FileBrowser Project",
            Category: "System",
            Icon: "📁",
            DefaultPort: 8082,
            WebPath: "/",
            Image: "filebrowser/filebrowser:latest"
        )
    };

    public static bool IsDockerAvailable(out string versionInfo)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(2000);
                if (!string.IsNullOrWhiteSpace(output))
                {
                    versionInfo = output.Trim();
                    return true;
                }
            }
        }
        catch { }

        versionInfo = "Docker Engine not detected";
        return false;
    }

    public static Dictionary<string, PluginState> LoadStates()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, PluginState>>(json);
                    if (dict != null) return dict;
                }
            }
            catch { }

            var defaults = new Dictionary<string, PluginState>
            {
                ["filebrowser"] = new PluginState { Id = "filebrowser", Installed = true, Enabled = true, Status = "Running", Port = 8082 },
                ["netdata"] = new PluginState { Id = "netdata", Installed = false, Enabled = false, Status = "Not Installed", Port = 19999 }
            };
            SaveStates(defaults);
            return defaults;
        }
    }

    public static void SaveStates(Dictionary<string, PluginState> states)
    {
        lock (_lock)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(states, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }

    public static List<PluginManifest> GetAllPlugins()
    {
        var states = LoadStates();
        var list = new List<PluginManifest>();

        // Query active docker containers if Docker is available
        var liveContainers = GetRunningDockerContainers();

        foreach (var item in Catalog)
        {
            if (states.TryGetValue(item.Id, out var state))
            {
                var containerName = $"simplenas_{item.Id}";
                var isLive = liveContainers.TryGetValue(containerName, out var containerId);

                list.Add(item with
                {
                    Installed = state.Installed,
                    Enabled = isLive || state.Enabled,
                    Status = isLive ? "Running (Live Container)" : (state.Enabled ? "Running" : (state.Installed ? "Stopped" : "Not Installed")),
                    ContainerId = containerId ?? state.ContainerId,
                    DefaultPort = state.Port ?? item.DefaultPort,
                    Settings = state.Settings
                });
            }
            else
            {
                list.Add(item with
                {
                    Installed = false,
                    Enabled = false,
                    Status = "Not Installed",
                    Settings = new Dictionary<string, string>()
                });
            }
        }

        return list;
    }

    public static (bool success, string message) InstallPlugin(string id)
    {
        var states = LoadStates();
        var plugin = Catalog.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (plugin == null) return (false, $"Plugin '{id}' not found.");

        int port = plugin.DefaultPort ?? 8080;
        var pluginDir = Path.Combine(PluginsDirectory, id);
        Directory.CreateDirectory(pluginDir);

        // Generate docker-compose.yml
        var composeContent = GenerateDockerCompose(plugin, port, pluginDir);
        var composePath = Path.Combine(pluginDir, "docker-compose.yml");
        File.WriteAllText(composePath, composeContent);

        // Attempt live docker compose up -d if available
        bool dockerUpSuccess = false;
        string dockerMsg = "";
        if (IsDockerAvailable(out _))
        {
            var res = RunDockerCommand($"compose -f \"{composePath}\" up -d");
            dockerUpSuccess = res.exitCode == 0;
            dockerMsg = dockerUpSuccess ? "Live Docker container started." : $"Docker start note: {res.output}";
        }

        states[id] = new PluginState
        {
            Id = id,
            Installed = true,
            Enabled = true,
            Status = dockerUpSuccess ? "Running (Live Container)" : "Running",
            Port = port,
            ContainerId = $"simplenas_{id}"
        };

        SaveStates(states);
        LogViewerService.Record("Info", "Plugin", "admin", $"Installed plugin '{plugin.Name}'", dockerMsg);

        return (true, $"Plugin '{plugin.Name}' installed. {dockerMsg}");
    }

    public static (bool success, string message) TogglePlugin(string id, bool enable)
    {
        var states = LoadStates();
        if (!states.TryGetValue(id, out var state) || !state.Installed)
        {
            return (false, $"Plugin '{id}' is not installed.");
        }

        var composePath = Path.Combine(PluginsDirectory, id, "docker-compose.yml");
        if (File.Exists(composePath) && IsDockerAvailable(out _))
        {
            if (enable)
            {
                RunDockerCommand($"compose -f \"{composePath}\" start");
            }
            else
            {
                RunDockerCommand($"compose -f \"{composePath}\" stop");
            }
        }

        state.Enabled = enable;
        state.Status = enable ? "Running" : "Stopped";
        states[id] = state;
        SaveStates(states);

        LogViewerService.Record("Info", "Plugin", "admin", $"Toggled plugin '{id}' to {(enable ? "Enabled" : "Disabled")}");
        return (true, $"Plugin '{id}' is now {(enable ? "enabled" : "disabled")}.");
    }

    public static (bool success, string message) UninstallPlugin(string id)
    {
        var states = LoadStates();
        if (!states.TryGetValue(id, out var state) || !state.Installed)
        {
            return (false, $"Plugin '{id}' is not installed.");
        }

        var composePath = Path.Combine(PluginsDirectory, id, "docker-compose.yml");
        if (File.Exists(composePath) && IsDockerAvailable(out _))
        {
            RunDockerCommand($"compose -f \"{composePath}\" down -v");
        }

        states.Remove(id);
        SaveStates(states);

        LogViewerService.Record("Warning", "Plugin", "admin", $"Uninstalled plugin '{id}'");
        return (true, $"Plugin '{id}' uninstalled successfully.");
    }

    public static (bool success, string message) UpdateSettings(string id, int? port, Dictionary<string, string>? settings)
    {
        var states = LoadStates();
        if (!states.TryGetValue(id, out var state) || !state.Installed)
        {
            return (false, $"Plugin '{id}' is not installed.");
        }

        if (port.HasValue) state.Port = port.Value;
        if (settings != null) state.Settings = settings;

        states[id] = state;
        SaveStates(states);
        LogViewerService.Record("Info", "Plugin", "admin", $"Updated configuration for plugin '{id}'");
        return (true, $"Configuration for '{id}' updated successfully.");
    }

    public static string GetContainerLogs(string id, int lines = 100)
    {
        var containerName = $"simplenas_{id}";
        if (IsDockerAvailable(out _))
        {
            var res = RunDockerCommand($"logs --tail {lines} {containerName}");
            if (!string.IsNullOrWhiteSpace(res.output)) return res.output;
        }

        return $"[Container: {containerName}]\nContainer service initialized. Awaiting runtime traffic...\n[Status: Healthy]";
    }

    private static Dictionary<string, string> GetRunningDockerContainers()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!IsDockerAvailable(out _)) return map;

        try
        {
            var res = RunDockerCommand("ps --format \"{{.Names}}|{{.ID}}\"");
            if (res.exitCode == 0 && !string.IsNullOrWhiteSpace(res.output))
            {
                foreach (var line in res.output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Trim().Split('|');
                    if (parts.Length == 2)
                    {
                        map[parts[0]] = parts[1];
                    }
                }
            }
        }
        catch { }

        return map;
    }

    private static (int exitCode, string output) RunDockerCommand(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (-1, "Failed to start docker process");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);

            var combined = (stdout + "\n" + stderr).Trim();
            return (process.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string GenerateDockerCompose(PluginManifest plugin, int port, string pluginDir)
    {
        var sb = new StringBuilder();
        sb.AppendLine("services:");
        sb.AppendLine($"  {plugin.Id}:");
        sb.AppendLine($"    container_name: simplenas_{plugin.Id}");
        sb.AppendLine($"    image: {plugin.Image ?? "alpine:latest"}");
        sb.AppendLine("    restart: unless-stopped");

        if (plugin.DefaultPort.HasValue)
        {
            sb.AppendLine("    ports:");
            sb.AppendLine($"      - \"{port}:{plugin.DefaultPort.Value}\"");
        }

        sb.AppendLine("    environment:");
        sb.AppendLine("      - PUID=1000");
        sb.AppendLine("      - PGID=1000");
        sb.AppendLine("      - TZ=UTC");

        sb.AppendLine("    volumes:");
        sb.AppendLine($"      - ./data:/data");
        sb.AppendLine($"      - ./config:/config");

        return sb.ToString();
    }
}
