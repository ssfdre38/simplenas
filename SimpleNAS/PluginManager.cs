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
    [property: JsonPropertyName("settings")] Dictionary<string, string>? Settings = null
);

public class PluginState
{
    public string Id { get; set; } = string.Empty;
    public bool Installed { get; set; }
    public bool Enabled { get; set; }
    public int? Port { get; set; }
    public string Status { get; set; } = "Inactive";
    public Dictionary<string, string> Settings { get; set; } = new();
}

public static class PluginManager
{
    private static readonly string ConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "plugins.json");
    private static readonly string PluginsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "plugins");
    private static readonly object _lock = new();

    // Default Curated Catalog of Plugins
    private static readonly List<PluginManifest> Catalog = new()
    {
        new PluginManifest(
            Id: "docker",
            Name: "Docker Container Engine",
            Description: "Container virtualization runtime for running microservices and isolated apps alongside SimpleNAS.",
            Version: "26.1.0",
            Author: "Docker Inc. / SimpleNAS",
            Category: "System",
            Icon: "🐳",
            DefaultPort: 2375,
            WebPath: null
        ),
        new PluginManifest(
            Id: "jellyfin",
            Name: "Jellyfin Media Server",
            Description: "Free, open-source software media system that puts you in control of managing and streaming your media.",
            Version: "10.9.11",
            Author: "Jellyfin Project",
            Category: "Media",
            Icon: "🎬",
            DefaultPort: 8096,
            WebPath: "/"
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
            WebPath: "/web"
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
            WebPath: null
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
            WebPath: "/transmission/web/"
        ),
        new PluginManifest(
            Id: "nextcloud",
            Name: "Nextcloud Hub",
            Description: "Self-hosted productivity platform providing private cloud storage, file synchronization, contacts, and calendar.",
            Version: "29.0.5",
            Author: "Nextcloud GmbH",
            Category: "Cloud & Backup",
            Icon: "☁️",
            DefaultPort: 8080,
            WebPath: "/"
        ),
        new PluginManifest(
            Id: "wireguard",
            Name: "WireGuard VPN Server",
            Description: "Extremely simple yet fast and modern VPN tunnel manager using state-of-the-art cryptography.",
            Version: "1.0.2",
            Author: "Jason A. Donenfeld",
            Category: "Network",
            Icon: "🛡️",
            DefaultPort: 51820,
            WebPath: null
        ),
        new PluginManifest(
            Id: "filebrowser",
            Name: "FileBrowser UI",
            Description: "Web-based file manager for SimpleNAS storage pools with file upload, download, and streaming.",
            Version: "2.30.0",
            Author: "FileBrowser Team",
            Category: "Tools",
            Icon: "📁",
            DefaultPort: 8082,
            WebPath: "/"
        )
    };

    static PluginManager()
    {
        try
        {
            if (!Directory.Exists(PluginsDirectory))
            {
                Directory.CreateDirectory(PluginsDirectory);
            }
        }
        catch { }
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

            // Default seed: Docker and FileBrowser installed out-of-the-box
            var defaults = new Dictionary<string, PluginState>
            {
                ["docker"] = new PluginState { Id = "docker", Installed = true, Enabled = true, Status = "Running", Port = 2375 },
                ["filebrowser"] = new PluginState { Id = "filebrowser", Installed = true, Enabled = true, Status = "Running", Port = 8082 }
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

        // Check for custom plugin manifests in plugins/ directory
        var customManifests = LoadCustomManifests();
        var combinedCatalog = new List<PluginManifest>(Catalog);
        foreach (var custom in customManifests)
        {
            if (!combinedCatalog.Any(c => c.Id.Equals(custom.Id, StringComparison.OrdinalIgnoreCase)))
            {
                combinedCatalog.Add(custom);
            }
        }

        foreach (var item in combinedCatalog)
        {
            if (states.TryGetValue(item.Id, out var state))
            {
                list.Add(item with
                {
                    Installed = state.Installed,
                    Enabled = state.Enabled,
                    Status = state.Enabled ? "Running" : (state.Installed ? "Stopped" : "Not Installed"),
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

    private static List<PluginManifest> LoadCustomManifests()
    {
        var results = new List<PluginManifest>();
        try
        {
            if (!Directory.Exists(PluginsDirectory)) return results;

            var subdirs = Directory.GetDirectories(PluginsDirectory);
            foreach (var dir in subdirs)
            {
                var manifestFile = Path.Combine(dir, "plugin.json");
                if (File.Exists(manifestFile))
                {
                    var json = File.ReadAllText(manifestFile);
                    var manifest = JsonSerializer.Deserialize<PluginManifest>(json);
                    if (manifest != null && !string.IsNullOrWhiteSpace(manifest.Id))
                    {
                        results.Add(manifest);
                    }
                }
            }
        }
        catch { }
        return results;
    }

    public static (bool success, string message) InstallPlugin(string id)
    {
        var states = LoadStates();
        var plugin = Catalog.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (plugin == null)
        {
            var custom = LoadCustomManifests().FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (custom == null) return (false, $"Plugin '{id}' not found in catalog.");
            plugin = custom;
        }

        if (states.TryGetValue(id, out var state) && state.Installed)
        {
            return (true, $"Plugin '{plugin.Name}' is already installed.");
        }

        states[id] = new PluginState
        {
            Id = id,
            Installed = true,
            Enabled = true,
            Status = "Running",
            Port = plugin.DefaultPort
        };

        SaveStates(states);
        return (true, $"Plugin '{plugin.Name}' installed and activated successfully.");
    }

    public static (bool success, string message) UninstallPlugin(string id)
    {
        var states = LoadStates();
        if (!states.TryGetValue(id, out var state) || !state.Installed)
        {
            return (false, $"Plugin '{id}' is not installed.");
        }

        states.Remove(id);
        SaveStates(states);
        return (true, $"Plugin '{id}' uninstalled successfully.");
    }

    public static (bool success, string message) TogglePlugin(string id, bool enable)
    {
        var states = LoadStates();
        if (!states.TryGetValue(id, out var state) || !state.Installed)
        {
            return (false, $"Plugin '{id}' is not installed.");
        }

        state.Enabled = enable;
        state.Status = enable ? "Running" : "Stopped";
        states[id] = state;
        SaveStates(states);

        return (true, $"Plugin '{id}' is now {(enable ? "enabled" : "disabled")}.");
    }

    public static (bool success, string message) UpdateSettings(string id, int? port, Dictionary<string, string> settings)
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
        return (true, $"Configuration for '{id}' updated successfully.");
    }
}
