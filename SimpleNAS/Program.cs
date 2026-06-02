using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8000");

// Add session support for authentication
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromHours(24);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".SimpleNAS.Session";
});

// Set content root to current directory so wwwroot is found
builder.Environment.ContentRootPath = Directory.GetCurrentDirectory();
builder.Environment.WebRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");

var app = builder.Build();

app.UseSession(); // Enable session middleware

// Configure static files - serve login.html without auth
var staticFileOptions = new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(app.Environment.WebRootPath),
    RequestPath = ""
};

app.UseDefaultFiles(); // Must come BEFORE UseStaticFiles()
app.UseStaticFiles(staticFileOptions);

// Simple auth middleware - check all /api requests except /api/auth/login
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && 
        !context.Request.Path.StartsWithSegments("/api/auth/login"))
    {
        var authenticated = context.Session.GetString("authenticated");
        if (authenticated != "true")
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
            return;
        }
    }
    await next();
});

string RunCommand(string command, params string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = command,
        Arguments = string.Join(" ", args),
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    using var process = Process.Start(psi);
    var output = process?.StandardOutput.ReadToEnd() ?? "";
    process?.WaitForExit();
    return output;
}

string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(bytes);
}

// Authentication endpoint
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<LoginRequest>();
    if (request == null) return Results.BadRequest(new { error = "Invalid request" });
    
    // Default credentials: admin / SimpleNAS2026
    var validUsername = Environment.GetEnvironmentVariable("SIMPLENAS_USER") ?? "admin";
    var validPasswordHash = Environment.GetEnvironmentVariable("SIMPLENAS_PASS_HASH") ?? 
        "e4lr09VYxD+hyWssAw3XCpOg9Ybx9qAGpTWFet7BE2w="; // SimpleNAS2026
    
    if (request.Username == validUsername && HashPassword(request.Password) == validPasswordHash)
    {
        context.Session.SetString("authenticated", "true");
        context.Session.SetString("username", request.Username);
        return Results.Ok(new { success = true, username = request.Username });
    }
    
    return Results.Unauthorized();
});

app.MapPost("/api/auth/logout", (HttpContext context) =>
{
    context.Session.Clear();
    return Results.Ok(new { success = true });
});

app.MapGet("/api/auth/status", (HttpContext context) =>
{
    var authenticated = context.Session.GetString("authenticated") == "true";
    var username = context.Session.GetString("username");
    return Results.Ok(new { authenticated, username });
});

app.MapPost("/api/auth/change-password", async (HttpContext context) =>
{
    var authenticated = context.Session.GetString("authenticated");
    if (authenticated != "true") return Results.Unauthorized();
    
    var request = await context.Request.ReadFromJsonAsync<ChangePasswordRequest>();
    if (request == null) return Results.BadRequest(new { error = "Invalid request" });
    
    var username = context.Session.GetString("username");
    var validUsername = Environment.GetEnvironmentVariable("SIMPLENAS_USER") ?? "admin";
    var currentHash = Environment.GetEnvironmentVariable("SIMPLENAS_PASS_HASH") ?? 
        "e4lr09VYxD+hyWssAw3XCpOg9Ybx9qAGpTWFet7BE2w=";
    
    // Verify current password
    if (username != validUsername || HashPassword(request.CurrentPassword) != currentHash)
    {
        return Results.Json(new { error = "Current password is incorrect" }, statusCode: 400);
    }
    
    // Save new password hash to file (persistent storage)
    var newHash = HashPassword(request.NewPassword);
    try {
        var credFile = "/opt/simplenas/.credentials";
        File.WriteAllText(credFile, $"USERNAME={username}\nPASSWORD_HASH={newHash}\n");
        RunCommand("chmod", "600", credFile);
        
        return Results.Ok(new { success = true, message = "Password changed. Restart SimpleNAS to apply changes." });
    } catch (Exception ex) {
        return Results.Json(new { error = $"Failed to save password: {ex.Message}" }, statusCode: 500);
    }
});

app.MapGet("/api/disks", () =>
{
    var output = RunCommand("lsblk", "-J", "-o", "NAME,SIZE,TYPE,MOUNTPOINT,MODEL");
    return Results.Json(JsonDocument.Parse(output).RootElement);
});

app.MapGet("/api/zfs/pools", () =>
{
    try {
        var output = RunCommand("zpool", "list", "-H");
        if (string.IsNullOrWhiteSpace(output))
            return Results.Json(new { pools = Array.Empty<object>() });
        
        var pools = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries)) // Tab-separated!
            .Where(fields => fields.Length >= 10)
            .Select(fields => new { 
                name = fields[0],       // NAME
                size = fields[1],       // SIZE
                alloc = fields[2],      // ALLOC
                allocated = fields[2],  // ALLOC (frontend expects 'allocated')
                free = fields[3],       // FREE
                capacity = fields[6],   // CAP
                health = fields[9]      // HEALTH
            })
            .ToList();
        return Results.Ok(new { pools });
    } catch {
        return Results.Json(new { pools = Array.Empty<object>() });
    }
});

app.MapPost("/api/zfs/pools", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ZfsPoolRequest>();
    if (request == null) return Results.BadRequest();
    var args = new List<string> { "create", request.Name };
    if (request.VdevType != "stripe") args.Add(request.VdevType);
    args.AddRange(request.Devices);
    RunCommand("zpool", args.ToArray());
    return Results.Ok(new { status = "created" });
});

app.MapGet("/api/zfs/devices", () =>
{
    try {
        var output = RunCommand("lsblk", "-d", "-n", "-o", "NAME,SIZE,TYPE");
        var devices = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length >= 3 && fields[2] == "disk")
            .Select(fields => new { name = "/dev/" + fields[0], size = fields[1] })
            .ToList();
        return Results.Ok(new { devices });
    } catch {
        return Results.Ok(new { devices = Array.Empty<object>() });
    }
});

app.MapGet("/api/system/status", () =>
{
    var dfOutput = RunCommand("df", "-h", "/");
    var lines = dfOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var diskPercent = "0%";
    if (lines.Length >= 2) {
        var fields = lines[1].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 5) diskPercent = fields[4];
    }
    
    double cpuPercent = 0.0;
    try {
        var mpstat = RunCommand("top", "-b", "-n", "1");
        var mpLines = mpstat.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var cpuLine = mpLines.FirstOrDefault(l => l.Contains("%Cpu(s)") || l.Contains("CPU:"));
        if (cpuLine != null) {
            // Very simple parser for top CPU output
            var idleIndex = cpuLine.IndexOf("id");
            if (idleIndex > 0) {
                var beforeIdle = cpuLine.Substring(0, idleIndex).Trim().Split(',').Last().Trim();
                var idleFields = beforeIdle.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (idleFields.Length > 0 && double.TryParse(idleFields.Last().Replace("%", "").Replace("id", "").Trim(), out double idle)) {
                    cpuPercent = 100.0 - idle;
                }
            }
        }
    } catch { }

    double memPercent = 0.0;
    try {
        var freeOutput = RunCommand("free", "-m");
        var memLines = freeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (memLines.Length >= 2) {
            var memFields = memLines[1].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (memFields.Length >= 3 && double.TryParse(memFields[1], out double total) && double.TryParse(memFields[2], out double used)) {
                memPercent = (used / total) * 100.0;
            }
        }
    } catch { }

    return Results.Json(new {
        cpu = new { percent = cpuPercent },
        memory = new { percent = memPercent },
        disk = new { percent = diskPercent }
    });
});

app.MapGet("/api/system/services", () =>
{
    var services = new[] { "smbd", "nmbd", "ssh", "tailscaled" };
    var status = services.ToDictionary(
        s => s,
        s => {
            try {
                var isActive = RunCommand("systemctl", "is-active", s).Trim();
                return isActive == "active" ? "active" : "inactive";
            } catch {
                return "inactive";
            }
        }
    );
    return Results.Ok(new { services = status });
});

app.MapGet("/api/network/interfaces", () =>
{
    try {
        var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Select(iface => new {
                name = iface.Name,
                state = iface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up ? "up" : "down",
                addresses = iface.GetIPProperties().UnicastAddresses
                    .Where(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(addr => new { address = addr.Address.ToString() })
                    .ToList()
            })
            .ToList();
        return Results.Ok(new { interfaces });
    } catch {
        return Results.Ok(new { interfaces = Array.Empty<object>() });
    }
});

app.MapGet("/api/network/tailscale/status", () =>
{
    try {
        var tailscalePath = RunCommand("which", "tailscale").Trim();
        var installed = !string.IsNullOrEmpty(tailscalePath);
        var running = false;
        if (installed) {
            var isActive = RunCommand("systemctl", "is-active", "tailscaled").Trim();
            running = isActive == "active";
        }
        return Results.Ok(new { installed, running });
    } catch {
        return Results.Ok(new { installed = false, running = false });
    }
});

app.MapPost("/api/network/tailscale/up", () =>
{
    try {
        RunCommand("systemctl", "start", "tailscaled");
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/network/tailscale/down", () =>
{
    try {
        RunCommand("systemctl", "stop", "tailscaled");
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// SMB Shares Endpoints
app.MapGet("/api/shares/smb", () =>
{
    var shares = ParseSmbConf(GetSmbConfPath());
    return Results.Ok(new { shares });
});

app.MapPost("/api/shares/smb", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateSmbShareRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Path))
        return Results.BadRequest(new { error = "Invalid request parameters" });

    try {
        AddSmbShare(GetSmbConfPath(), request);
        if (OperatingSystem.IsLinux()) {
            RunCommand("systemctl", "reload", "smbd");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/shares/smb/{name}", (string name) =>
{
    if (string.IsNullOrWhiteSpace(name))
        return Results.BadRequest(new { error = "Invalid share name" });

    try {
        DeleteSmbShare(GetSmbConfPath(), name);
        if (OperatingSystem.IsLinux()) {
            RunCommand("systemctl", "reload", "smbd");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// NFS Exports Endpoints
app.MapGet("/api/shares/nfs", () =>
{
    var exports = ParseNfsExports(GetNfsExportsPath());
    return Results.Ok(new { exports });
});

app.MapPost("/api/shares/nfs", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateNfsExportRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Path) || request.Clients == null || !request.Clients.Any())
        return Results.BadRequest(new { error = "Invalid request parameters" });

    try {
        AddNfsExport(GetNfsExportsPath(), request);
        if (OperatingSystem.IsLinux()) {
            RunCommand("exportfs", "-ar");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/shares/nfs", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<DeleteNfsExportRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Path))
        return Results.BadRequest(new { error = "Invalid request parameters" });

    try {
        DeleteNfsExport(GetNfsExportsPath(), request.Path);
        if (OperatingSystem.IsLinux()) {
            RunCommand("exportfs", "-ar");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// Service controls API
app.MapPost("/api/system/services/control", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ServiceControlRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Service) || string.IsNullOrWhiteSpace(request.Action))
        return Results.BadRequest(new { error = "Invalid request parameters" });

    var allowedServices = new[] { "smbd", "nmbd", "ssh", "tailscaled" };
    var allowedActions = new[] { "start", "stop", "restart" };

    if (!allowedServices.Contains(request.Service) || !allowedActions.Contains(request.Action))
        return Results.BadRequest(new { error = "Unauthorized service or action" });

    try {
        if (OperatingSystem.IsLinux()) {
            RunCommand("systemctl", request.Action, request.Service);
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// ZFS datasets API
app.MapGet("/api/zfs/datasets", () =>
{
    try {
        var output = RunCommand("zfs", "list", "-H");
        if (string.IsNullOrWhiteSpace(output))
            return Results.Ok(new { datasets = Array.Empty<object>() });

        var datasets = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new {
                name = fields[0],      // NAME
                used = fields[1],      // USED
                avail = fields[2],     // AVAIL
                mountpoint = fields[3] // MOUNTPOINT
            })
            .ToList();
        return Results.Ok(new { datasets });
    } catch {
        return Results.Ok(new { datasets = Array.Empty<object>() });
    }
});

// Firewall status API
app.MapGet("/api/network/firewall", () =>
{
    try {
        var output = RunCommand("ufw", "status").Trim();
        var active = output.Contains("Status: active");
        return Results.Ok(new { active });
    } catch {
        return Results.Ok(new { active = false });
    }
});

// Firewall toggle API
app.MapPost("/api/network/firewall/toggle", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<FirewallToggleRequest>();
    if (request == null) return Results.BadRequest();

    try {
        if (OperatingSystem.IsLinux()) {
            if (request.Enable) {
                RunCommand("bash", "-c", "echo 'y' | ufw enable");
            } else {
                RunCommand("ufw", "disable");
            }
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

Console.WriteLine("SimpleNAS running on http://0.0.0.0:8000");
Console.WriteLine("Default login: admin / SimpleNAS2026");
app.Run();

// File Path Helpers
string GetSmbConfPath()
{
    if (OperatingSystem.IsLinux()) return "/etc/samba/smb.conf";
    var localPath = Path.Combine(Directory.GetCurrentDirectory(), "smb.conf");
    if (!File.Exists(localPath)) File.WriteAllText(localPath, "[global]\n\tworkgroup = WORKGROUP\n");
    return localPath;
}

string GetNfsExportsPath()
{
    if (OperatingSystem.IsLinux()) return "/etc/exports";
    var localPath = Path.Combine(Directory.GetCurrentDirectory(), "exports");
    if (!File.Exists(localPath)) File.WriteAllText(localPath, "# NFS exports\n");
    return localPath;
}

// INI-like Samba config parser
List<SmbShare> ParseSmbConf(string filePath)
{
    var shares = new List<SmbShare>();
    if (!File.Exists(filePath)) return shares;
    
    var lines = File.ReadAllLines(filePath);
    string? currentSection = null;
    var currentConfig = new Dictionary<string, string>();
    
    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith(";") || line.StartsWith("#"))
            continue;
            
        if (line.StartsWith("[") && line.EndsWith("]"))
        {
            if (currentSection != null && currentSection != "global" && currentSection != "printers" && currentSection != "print$")
            {
                shares.Add(new SmbShare(currentSection, new Dictionary<string, string>(currentConfig)));
            }
            currentSection = line.Substring(1, line.Length - 2).Trim();
            currentConfig.Clear();
        }
        else if (currentSection != null)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                currentConfig[key] = value;
            }
        }
    }
    
    if (currentSection != null && currentSection != "global" && currentSection != "printers" && currentSection != "print$")
    {
        shares.Add(new SmbShare(currentSection, new Dictionary<string, string>(currentConfig)));
    }
    
    return shares;
}

void AddSmbShare(string filePath, CreateSmbShareRequest share)
{
    var sb = new StringBuilder();
    sb.AppendLine();
    sb.AppendLine($"[{share.Name}]");
    sb.AppendLine($"\tpath = {share.Path}");
    sb.AppendLine($"\tread only = {(share.ReadOnly ? "yes" : "no")}");
    sb.AppendLine($"\tguest ok = {(share.GuestOk ? "yes" : "no")}");
    sb.AppendLine($"\tcreate mask = 0775");
    sb.AppendLine($"\tdirectory mask = 0775");
    
    File.AppendAllText(filePath, sb.ToString());
}

void DeleteSmbShare(string filePath, string shareName)
{
    if (!File.Exists(filePath)) return;
    var lines = File.ReadAllLines(filePath);
    var newLines = new List<string>();
    bool insideTargetSection = false;
    
    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (line.StartsWith("[") && line.EndsWith("]"))
        {
            var sectionName = line.Substring(1, line.Length - 2).Trim();
            if (sectionName == shareName)
            {
                insideTargetSection = true;
                continue;
            }
            else
            {
                insideTargetSection = false;
            }
        }
        
        if (insideTargetSection) continue;
        newLines.Add(rawLine);
    }
    
    File.WriteAllLines(filePath, newLines);
}

// NFS exports file parser
List<NfsExport> ParseNfsExports(string filePath)
{
    var exports = new List<NfsExport>();
    if (!File.Exists(filePath)) return exports;
    
    var lines = File.ReadAllLines(filePath);
    foreach (var rawLine in lines)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
            continue;
            
        var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1)
        {
            var path = parts[0];
            var clients = parts.Skip(1).ToList();
            exports.Add(new NfsExport(path, clients));
        }
    }
    return exports;
}

void AddNfsExport(string filePath, CreateNfsExportRequest export)
{
    var clientStr = string.Join(" ", export.Clients.Select(c => $"{c}({export.Options})"));
    var line = $"\n{export.Path} {clientStr}";
    File.AppendAllText(filePath, line);
}

void DeleteNfsExport(string filePath, string path)
{
    if (!File.Exists(filePath)) return;
    var lines = File.ReadAllLines(filePath);
    var newLines = lines.Where(rawLine => {
        var line = rawLine.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith("#")) return true;
        var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 1 && parts[0].TrimEnd('/') == path.TrimEnd('/')) return false;
        return true;
    }).ToList();
    
    File.WriteAllLines(filePath, newLines);
}

record ZfsPoolRequest(string Name, string VdevType, string[] Devices);
record LoginRequest(string Username, string Password);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
record CreateSmbShareRequest(string Name, string Path, bool ReadOnly, bool GuestOk);
record CreateNfsExportRequest(string Path, List<string> Clients, string Options);
record DeleteNfsExportRequest(string Path);
record SmbShare(string Name, Dictionary<string, string> Config);
record NfsExport(string Path, List<string> Clients);
record ServiceControlRequest(string Service, string Action);
record FirewallToggleRequest(bool Enable);
