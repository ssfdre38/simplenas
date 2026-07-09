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
        return Results.Json(new { pools });
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

app.MapGet("/api/system/status", () =>
{
    var dfOutput = RunCommand("df", "-h", "/");
    var lines = dfOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    var diskPercent = "0%";
    if (lines.Length >= 2) {
        var fields = lines[1].Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 5) diskPercent = fields[4];
    }

    double memPercent = 0.0;
    try {
        if (File.Exists("/proc/meminfo")) {
            var memLines = File.ReadAllLines("/proc/meminfo");
            double total = 0, available = 0;
            foreach (var line in memLines) {
                if (line.StartsWith("MemTotal:")) {
                    total = double.Parse(System.Text.RegularExpressions.Regex.Replace(line, "[^0-9]", ""));
                } else if (line.StartsWith("MemAvailable:")) {
                    available = double.Parse(System.Text.RegularExpressions.Regex.Replace(line, "[^0-9]", ""));
                }
            }
            if (total > 0) {
                memPercent = ((total - available) / total) * 100.0;
            }
        }
    } catch {}

    double cpuPercent = 0.0;
    try {
        if (File.Exists("/proc/stat")) {
            var getUsage = () => {
                var statLine = File.ReadLines("/proc/stat").First();
                var parts = statLine.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(double.Parse).ToArray();
                var idle = parts[3] + parts[4]; // idle + iowait
                var total = parts.Sum();
                return (idle, total);
            };
            var (idle1, total1) = getUsage();
            Thread.Sleep(100);
            var (idle2, total2) = getUsage();
            var totalDelta = total2 - total1;
            var idleDelta = idle2 - idle1;
            if (totalDelta > 0) {
                cpuPercent = (1.0 - (idleDelta / totalDelta)) * 100.0;
            }
        }
    } catch {}

    return Results.Json(new {
        cpu = new { percent = cpuPercent },
        memory = new { percent = memPercent },
        disk = new { percent = diskPercent }
    });
});

app.MapGet("/api/system/services", () =>
{
    var getStatus = (string serviceName) => {
        var output = RunCommand("systemctl", "is-active", serviceName).Trim();
        return output == "active" ? "active" : "inactive";
    };
    return Results.Json(new {
        services = new Dictionary<string, string> {
            { "ZFS Mount Service", getStatus("zfs-mount") },
            { "Samba Share (SMB)", getStatus("smbd") },
            { "NFS Share Server", getStatus("nfs-kernel-server") },
            { "Tailscale VPN", getStatus("tailscaled") }
        }
    });
});

// Cloud Storage Manager
bool isSyncActive = false;

app.MapGet("/api/cloud/status", () =>
{
    var mountOutput = RunCommand("mount");
    bool rcloneMounted = mountOutput.Contains("/mnt/gdrive");
    bool mergerfsMounted = mountOutput.Contains("/mnt/tank/unified");
    
    return Results.Json(new {
        rcloneMounted = rcloneMounted,
        rclonePath = "/mnt/gdrive",
        mergerfsMounted = mergerfsMounted,
        mergerfsPath = "/mnt/tank/unified",
        syncActive = isSyncActive
    });
});

app.MapPost("/api/cloud/mount", () =>
{
    try {
        if (!Directory.Exists("/mnt/gdrive")) Directory.CreateDirectory("/mnt/gdrive");
        
        Task.Run(() => {
            RunCommand("rclone", "mount", "gdrive:", "/mnt/gdrive", "--vfs-cache-mode", "writes", "--allow-other", "--daemon");
        });
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/unmount", () =>
{
    try {
        RunCommand("fusermount", "-u", "/mnt/gdrive");
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/union", () =>
{
    try {
        if (!Directory.Exists("/mnt/tank/unified")) Directory.CreateDirectory("/mnt/tank/unified");
        
        Task.Run(() => {
            RunCommand("mergerfs", "-o", "defaults,allow_other,use_ino,category.create=ff", "/mnt/tank/local:/mnt/gdrive", "/mnt/tank/unified");
        });
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/unmount-union", () =>
{
    try {
        RunCommand("fusermount", "-u", "/mnt/tank/unified");
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/sync", () =>
{
    if (isSyncActive) return Results.Conflict(new { error = "Sync task is already running." });
    
    isSyncActive = true;
    Task.Run(() => {
        try {
            RunCommand("rclone", "move", "/mnt/tank/local/Backups", "gdrive:Backups", "--min-age", "30d");
        } finally {
            isSyncActive = false;
        }
    });
    return Results.Ok(new { success = true });
});

Console.WriteLine("SimpleNAS running on http://0.0.0.0:8000");
Console.WriteLine("Default login: admin / SimpleNAS2026");
app.Run();

record ZfsPoolRequest(string Name, string VdevType, string[] Devices);
record LoginRequest(string Username, string Password);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
