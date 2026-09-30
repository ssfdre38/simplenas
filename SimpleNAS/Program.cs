using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using SimpleNAS;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<SnapshotSchedulerService>();
var defaultUrl = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8000";
builder.WebHost.UseUrls(defaultUrl);

// Optional Kestrel HTTPS configuration if certificate is installed
var sslConfig = SslManager.GetConfig();
var pfxPath = Path.Combine(Directory.GetCurrentDirectory(), "simplenas_cert.pfx");
if (sslConfig.Enabled && File.Exists(pfxPath))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(8000);
        try
        {
            options.ListenAnyIP(sslConfig.HttpsPort, listenOptions =>
            {
                listenOptions.UseHttps(pfxPath, sslConfig.PfxPassword);
            });
        }
        catch { }
    });
}

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

// Simple auth middleware - check all /api requests except /api/auth/login, allow ACME challenge
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/.well-known"))
    {
        await next();
        return;
    }

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

        // Role-based authorization for Viewer role (prevent POST/DELETE on destructive endpoints)
        var role = context.Session.GetString("role") ?? "Admin";
        if (role == "Viewer" && (context.Request.Method == "POST" || context.Request.Method == "DELETE"))
        {
            if (!context.Request.Path.StartsWithSegments("/api/auth/logout"))
            {
                context.Response.StatusCode = 403;
                await context.Response.WriteAsJsonAsync(new { error = "Forbidden: Viewer accounts have read-only access." });
                return;
            }
        }
    }
    await next();
});

string ResolveCommandPath(string command)
{
    if (!OperatingSystem.IsLinux()) return command;
    
    var searchPaths = new[] { "/usr/sbin", "/sbin", "/usr/bin", "/bin" };
    foreach (var path in searchPaths)
    {
        var fullPath = Path.Combine(path, command);
        if (File.Exists(fullPath)) return fullPath;
    }
    return command;
}

string RunCommand(string command, params string[] args)
{
    try
    {
        var resolvedCommand = ResolveCommandPath(command);
        var psi = new ProcessStartInfo
        {
            FileName = resolvedCommand,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            if (!string.IsNullOrEmpty(arg))
            {
                psi.ArgumentList.Add(arg);
            }
        }
        using var process = Process.Start(psi);
        if (process == null) return "";
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return stdoutTask.GetAwaiter().GetResult();
    }
    catch
    {
        return "";
    }
}

string RunCommandWithInput(string command, string input, params string[] args)
{
    try
    {
        var resolvedCommand = ResolveCommandPath(command);
        var psi = new ProcessStartInfo
        {
            FileName = resolvedCommand,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
        {
            if (!string.IsNullOrEmpty(arg))
            {
                psi.ArgumentList.Add(arg);
            }
        }
        using var process = Process.Start(psi);
        if (process == null) return "";
        using (var sw = process.StandardInput)
        {
            sw.Write(input);
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return stdoutTask.GetAwaiter().GetResult();
    }
    catch
    {
        return "";
    }
}

string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(bytes);
}

string GetCredentialsFilePath()
{
    if (OperatingSystem.IsLinux()) return "/opt/simplenas/.credentials";
    return Path.Combine(Directory.GetCurrentDirectory(), ".credentials");
}

(string Username, string PasswordHash) GetCurrentCredentials()
{
    string? username = Environment.GetEnvironmentVariable("SIMPLENAS_USER");
    string? passwordHash = Environment.GetEnvironmentVariable("SIMPLENAS_PASS_HASH");

    var credFile = GetCredentialsFilePath();
    if (File.Exists(credFile))
    {
        try
        {
            var lines = File.ReadAllLines(credFile);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("USERNAME=")) username = trimmed["USERNAME=".Length..].Trim();
                else if (trimmed.StartsWith("PASSWORD_HASH=")) passwordHash = trimmed["PASSWORD_HASH=".Length..].Trim();
            }
        }
        catch { }
    }

    username ??= "admin";
    passwordHash ??= "e4lr09VYxD+hyWssAw3XCpOg9Ybx9qAGpTWFet7BE2w="; // SimpleNAS2026

    return (username, passwordHash);
}

string GetUsersJsonPath() => Path.Combine(Directory.GetCurrentDirectory(), "users.json");

List<NasUserRecord> GetRegisteredUsers()
{
    var path = GetUsersJsonPath();
    var list = new List<NasUserRecord>();
    if (File.Exists(path))
    {
        try
        {
            var json = File.ReadAllText(path);
            var users = JsonSerializer.Deserialize<List<NasUserRecord>>(json);
            if (users != null) list = users;
        }
        catch { }
    }
    var creds = GetCurrentCredentials();
    if (!list.Any(u => u.Username.Equals(creds.Username, StringComparison.OrdinalIgnoreCase)))
    {
        list.Insert(0, new NasUserRecord(creds.Username, creds.PasswordHash, "Admin"));
    }
    return list;
}

void SaveRegisteredUsers(List<NasUserRecord> users)
{
    var path = GetUsersJsonPath();
    var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

// ACME HTTP-01 challenge responder for Let's Encrypt
app.MapGet("/.well-known/acme-challenge/{token}", (string token) =>
{
    if (SslManager.AcmeChallenges.TryGetValue(token, out var keyAuth))
    {
        return Results.Text(keyAuth, "text/plain");
    }
    return Results.NotFound();
});

// Authentication endpoint
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<LoginRequest>();
    if (request == null) return Results.BadRequest(new { error = "Invalid request" });
    
    var users = GetRegisteredUsers();
    var hash = HashPassword(request.Password);
    var matchedUser = users.FirstOrDefault(u => 
        u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase) && 
        u.PasswordHash == hash);

    if (matchedUser != null)
    {
        context.Session.SetString("authenticated", "true");
        context.Session.SetString("username", matchedUser.Username);
        context.Session.SetString("role", matchedUser.Role);
        return Results.Ok(new { success = true, username = matchedUser.Username, role = matchedUser.Role });
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
    var role = context.Session.GetString("role") ?? "Admin";
    return Results.Ok(new { authenticated, username, role });
});

app.MapPost("/api/auth/change-password", async (HttpContext context) =>
{
    var authenticated = context.Session.GetString("authenticated");
    if (authenticated != "true") return Results.Unauthorized();
    
    var request = await context.Request.ReadFromJsonAsync<ChangePasswordRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        return Results.BadRequest(new { error = "Current password and new password are required." });
    
    var sessionUsername = context.Session.GetString("username") ?? "admin";
    var users = GetRegisteredUsers();
    var user = users.FirstOrDefault(u => u.Username.Equals(sessionUsername, StringComparison.OrdinalIgnoreCase));
    
    if (user == null)
    {
        return Results.NotFound(new { error = "User account not found." });
    }

    // Verify current password hash
    if (HashPassword(request.CurrentPassword) != user.PasswordHash)
    {
        return Results.Json(new { error = "Current password is incorrect." }, statusCode: 400);
    }
    
    var newHash = HashPassword(request.NewPassword);
    try {
        // Update user record in users.json
        users.RemoveAll(u => u.Username.Equals(sessionUsername, StringComparison.OrdinalIgnoreCase));
        users.Add(user with { PasswordHash = newHash });
        SaveRegisteredUsers(users);

        // If admin or root credentials account, also update .credentials
        var creds = GetCurrentCredentials();
        if (sessionUsername.Equals("admin", StringComparison.OrdinalIgnoreCase) || sessionUsername.Equals(creds.Username, StringComparison.OrdinalIgnoreCase))
        {
            var credFile = GetCredentialsFilePath();
            var dir = Path.GetDirectoryName(credFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(credFile, $"USERNAME={sessionUsername}\nPASSWORD_HASH={newHash}\n");
            if (OperatingSystem.IsLinux()) RunCommand("chmod", "600", credFile);
        }

        // On Linux, synchronize Samba password
        if (OperatingSystem.IsLinux()) {
            RunCommandWithInput("smbpasswd", $"{request.NewPassword}\n{request.NewPassword}\n", "-s", sessionUsername);
        }
        
        return Results.Ok(new { success = true, message = "Password changed successfully." });
    } catch (Exception ex) {
        return Results.Json(new { error = $"Failed to save password: {ex.Message}" }, statusCode: 500);
    }
});

app.MapGet("/api/disks", () =>
{
    return Results.Json(SystemTelemetry.GetBlockDevices((cmd, args) => RunCommand(cmd, args)));
});

app.MapGet("/api/zfs/pools", () =>
{
    try {
        var pools = SystemTelemetry.GetStoragePools((cmd, args) => RunCommand(cmd, args), GetPoolsFilePath());
        return Results.Ok(new { pools });
    } catch {
        return Results.Json(new { pools = Array.Empty<object>() });
    }
});

app.MapPost("/api/zfs/pools", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ZfsPoolRequest>();
    if (request == null) return Results.BadRequest();
    
    if (OperatingSystem.IsLinux()) {
        var args = new List<string> { "create", request.Name };
        if (request.VdevType != "stripe") args.Add(request.VdevType);
        args.AddRange(request.Devices);
        RunCommand("zpool", args.ToArray());
    } else {
        var path = GetPoolsFilePath();
        var size = "3.6T";
        File.AppendAllText(path, $"{request.Name}\t{size}\t0B\t{size}\t-\t0%\t0%\t1.00x\tONLINE\t-\n");

        var dsPath = GetDatasetsFilePath();
        var mountpoint = OperatingSystem.IsWindows() ? $"D:\\{request.Name}" : $"/{request.Name}";
        if (OperatingSystem.IsWindows())
        {
            try { if (!Directory.Exists(mountpoint)) Directory.CreateDirectory(mountpoint); } catch { }
        }
        File.AppendAllText(dsPath, $"{request.Name}\t0B\t{size}\t{mountpoint}\n");

        // Remove used devices from mock devices
        var devPath = GetDevicesFilePath();
        if (File.Exists(devPath)) {
            var lines = File.ReadAllLines(devPath);
            var updatedLines = lines.Where(l => {
                var parts = l.Split('\t');
                if (parts.Length > 0) {
                    var fullName = "/dev/" + parts[0];
                    return !request.Devices.Contains(fullName);
                }
                return true;
            });
            File.WriteAllLines(devPath, updatedLines);
        }
    }
    return Results.Ok(new { status = "created" });
});

app.MapDelete("/api/zfs/pools/{name}", (string name) =>
{
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest();

    if (OperatingSystem.IsLinux()) {
        RunCommand("zpool", "destroy", name);
    } else {
        var path = GetPoolsFilePath();
        if (File.Exists(path)) {
            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith(name + "\t")).ToList();
            File.WriteAllLines(path, lines);
        }
        // Also remove datasets belonging to this pool
        var dsPath = GetDatasetsFilePath();
        if (File.Exists(dsPath)) {
            var lines = File.ReadAllLines(dsPath).Where(l => !l.StartsWith(name + "\t") && !l.StartsWith(name + "/")).ToList();
            File.WriteAllLines(dsPath, lines);
        }
        // Restore mock devices
        var devPath = GetDevicesFilePath();
        File.WriteAllText(devPath, "sdb\t2.0T\tdisk\nsdc\t2.0T\tdisk\nsdd\t2.0T\tdisk\n");
    }
    return Results.Ok(new { success = true });
});

app.MapGet("/api/zfs/devices", () =>
{
    var devices = SystemTelemetry.GetRawDevices((cmd, args) => RunCommand(cmd, args), GetDevicesFilePath());
    return Results.Ok(new { devices });
});

app.MapGet("/api/system/status", () =>
{
    var cpuPercent = SystemTelemetry.GetCpuPercent();
    var memPercent = SystemTelemetry.GetMemoryPercent();
    var diskPercent = SystemTelemetry.GetDiskPercent();

    return Results.Json(new {
        platform = OperatingSystem.IsWindows() ? "windows" : (OperatingSystem.IsLinux() ? "linux" : "unknown"),
        osDescription = RuntimeInformation.OSDescription,
        cpu = new { percent = cpuPercent },
        memory = new { percent = memPercent },
        disk = new { percent = diskPercent }
    });
});

app.MapGet("/api/system/services", () =>
{
    var status = SystemTelemetry.GetServicesStatus((cmd, args) => RunCommand(cmd, args));
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
    var (installed, running) = SystemTelemetry.GetTailscaleStatus((cmd, args) => RunCommand(cmd, args));
    return Results.Ok(new { installed, running });
});

app.MapPost("/api/network/tailscale/up", () =>
{
    try {
        SystemTelemetry.SetTailscale(true, (cmd, args) => RunCommand(cmd, args));
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/network/tailscale/down", () =>
{
    try {
        SystemTelemetry.SetTailscale(false, (cmd, args) => RunCommand(cmd, args));
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// SMB Shares Endpoints
app.MapGet("/api/shares/smb", () =>
{
    var shares = SystemTelemetry.GetSmbShares((cmd, args) => RunCommand(cmd, args), GetSmbConfPath(), ParseSmbConf);
    return Results.Ok(new { shares });
});

app.MapPost("/api/shares/smb", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateSmbShareRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Path))
        return Results.BadRequest(new { error = "Invalid request parameters" });

    try {
        if (OperatingSystem.IsWindows()) {
            SystemTelemetry.AddShare(request.Name, request.Path, request.ReadOnly, (cmd, args) => RunCommand(cmd, args));
        }
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
        if (OperatingSystem.IsWindows()) {
            SystemTelemetry.DeleteShare(name, (cmd, args) => RunCommand(cmd, args));
        }
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
        SystemTelemetry.ControlService(request.Service, request.Action, (cmd, args) => RunCommand(cmd, args));
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// Storage datasets API
app.MapGet("/api/zfs/datasets", () =>
{
    try {
        var datasets = SystemTelemetry.GetStorageDatasets((cmd, args) => RunCommand(cmd, args), GetDatasetsFilePath());
        return Results.Ok(new { datasets });
    } catch {
        return Results.Ok(new { datasets = Array.Empty<object>() });
    }
});

app.MapPost("/api/zfs/datasets", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateDatasetRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Pool) || string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Invalid dataset parameters" });

    var fullName = $"{request.Pool}/{request.Name}";
    if (OperatingSystem.IsLinux()) {
        RunCommand("zfs", "create", fullName);
    } else {
        var path = GetDatasetsFilePath();
        string mountpoint;
        if (OperatingSystem.IsWindows())
        {
            var letter = "D";
            if (request.Pool.Contains('_'))
            {
                var candidate = request.Pool.Split('_').Last();
                if (candidate.Length == 1 && char.IsLetter(candidate[0])) letter = candidate.ToUpperInvariant();
            }
            mountpoint = $"{letter}:\\{request.Name}";
            try
            {
                if (!Directory.Exists(mountpoint)) Directory.CreateDirectory(mountpoint);
            }
            catch { }
        }
        else
        {
            mountpoint = $"/{fullName}";
        }
        File.AppendAllText(path, $"{fullName}\t0B\t3.6T\t{mountpoint}\n");
    }
    return Results.Ok(new { success = true });
});

app.MapDelete("/api/zfs/datasets/{*name}", (string name) =>
{
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest();

    if (OperatingSystem.IsLinux()) {
        RunCommand("zfs", "destroy", "-r", name);
    } else {
        var path = GetDatasetsFilePath();
        if (File.Exists(path)) {
            var lines = File.ReadAllLines(path).Where(l => {
                var parts = l.Split('\t');
                if (parts.Length > 0) {
                    var dsName = parts[0];
                    return dsName != name && !dsName.StartsWith(name + "/");
                }
                return true;
            }).ToList();
            File.WriteAllLines(path, lines);
        }
        // Also remove snapshots for this dataset
        var snapPath = GetSnapshotsFilePath();
        if (File.Exists(snapPath)) {
            var lines = File.ReadAllLines(snapPath).Where(l => {
                var parts = l.Split('\t');
                if (parts.Length > 0) {
                    return !parts[0].StartsWith(name + "@");
                }
                return true;
            }).ToList();
            File.WriteAllLines(snapPath, lines);
        }
    }
    return Results.Ok(new { success = true });
});

app.MapGet("/api/zfs/snapshots", () =>
{
    try {
        string output;
        if (OperatingSystem.IsLinux()) {
            output = RunCommand("zfs", "list", "-t", "snapshot", "-H");
        } else {
            var path = GetSnapshotsFilePath();
            if (!File.Exists(path)) {
                File.WriteAllText(path, "tank/media@backup_2026_06_01\t12M\t-\t450G\t-\ntank/media@backup_2026_06_02\t2M\t-\t450G\t-\n");
            }
            output = File.ReadAllText(path);
        }

        if (string.IsNullOrWhiteSpace(output))
            return Results.Ok(new { snapshots = Array.Empty<object>() });

        var snapshots = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) // Splits by any whitespace!
            .Where(fields => fields.Length >= 4)
            .Select(fields => new {
                name = fields[0],      // NAME
                used = fields[1],      // USED
                refer = fields.Length >= 4 ? fields[3] : fields[1] // REFER (index 3 in 5-column whitespace split)
            })
            .ToList();
        return Results.Ok(new { snapshots });
    } catch {
        return Results.Ok(new { snapshots = Array.Empty<object>() });
    }
});

app.MapPost("/api/zfs/snapshots", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateSnapshotRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Dataset) || string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest(new { error = "Invalid snapshot parameters" });

    var fullName = $"{request.Dataset}@{request.Name}";
    if (OperatingSystem.IsLinux()) {
        RunCommand("zfs", "snapshot", fullName);
    } else {
        var path = GetSnapshotsFilePath();
        File.AppendAllText(path, $"{fullName}\t0B\t-\t0B\t-\n");
    }
    return Results.Ok(new { success = true });
});

app.MapPost("/api/zfs/snapshots/rollback", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<RollbackSnapshotRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Snapshot))
        return Results.BadRequest(new { error = "Invalid snapshot parameters" });

    if (OperatingSystem.IsLinux()) {
        RunCommand("zfs", "rollback", "-r", request.Snapshot);
    }
    return Results.Ok(new { success = true });
});

app.MapDelete("/api/zfs/snapshots/{*name}", (string name) =>
{
    if (string.IsNullOrWhiteSpace(name)) return Results.BadRequest();

    if (OperatingSystem.IsLinux()) {
        RunCommand("zfs", "destroy", name);
    } else {
        var path = GetSnapshotsFilePath();
        if (File.Exists(path)) {
            var lines = File.ReadAllLines(path).Where(l => {
                var parts = l.Split('\t');
                if (parts.Length > 0) {
                    return parts[0] != name;
                }
                return true;
            }).ToList();
            File.WriteAllLines(path, lines);
        }
    }
    return Results.Ok(new { success = true });
});

app.MapPost("/api/zfs/pools/scrub", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ScrubPoolRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Pool))
        return Results.BadRequest(new { error = "Invalid pool parameter" });

    if (OperatingSystem.IsLinux()) {
        RunCommand("zpool", "scrub", request.Pool);
    }
    return Results.Ok(new { success = true });
});


// Firewall status API
app.MapGet("/api/network/firewall", () =>
{
    var active = SystemTelemetry.GetFirewallActive((cmd, args) => RunCommand(cmd, args));
    return Results.Ok(new { active });
});

// Firewall toggle API
app.MapPost("/api/network/firewall/toggle", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<FirewallToggleRequest>();
    if (request == null) return Results.BadRequest();

    try {
        SystemTelemetry.SetFirewallActive(request.Enable, (cmd, args) => RunCommand(cmd, args));
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// Get user's current connection IP
app.MapGet("/api/network/myip", (HttpContext context) =>
{
    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    // Check for IPv6 loopback
    if (ip == "::1" || ip == "127.0.0.1") ip = "127.0.0.1";
    // Strip IPv6 to IPv4 mapping prefix if present (e.g. ::ffff:192.168.1.1)
    if (ip.StartsWith("::ffff:")) ip = ip.Substring(7);
    return Results.Ok(new { ip });
});

// Add Firewall whitelist rule
app.MapPost("/api/network/firewall/whitelist", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<WhitelistRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Ip))
        return Results.BadRequest(new { error = "Invalid IP address" });

    try {
        if (OperatingSystem.IsLinux()) {
            var args = new List<string> { "allow", "from", request.Ip };
            if (request.Port > 0) {
                args.AddRange(["to", "any", "port", request.Port.ToString(), "proto", "tcp"]);
            }
            if (!string.IsNullOrWhiteSpace(request.Comment)) {
                args.AddRange(["comment", request.Comment]);
            }
            RunCommand("ufw", args.ToArray());
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// User Management APIs with Role-Based Access Control (RBAC)
app.MapGet("/api/users", () =>
{
    var users = GetRegisteredUsers().Select(u => new { username = u.Username, role = u.Role }).ToList();
    return Results.Ok(new { users });
});

app.MapPost("/api/users", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<CreateUserRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest(new { error = "Invalid user parameters" });

    var role = string.IsNullOrWhiteSpace(request.Role) ? "Operator" : request.Role;
    if (role != "Admin" && role != "Operator" && role != "Viewer") role = "Operator";

    try {
        var users = GetRegisteredUsers();
        if (users.Any(u => u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase)))
            return Results.BadRequest(new { error = "User already exists" });

        var hash = HashPassword(request.Password);
        users.Add(new NasUserRecord(request.Username, hash, role));
        SaveRegisteredUsers(users);

        if (OperatingSystem.IsLinux()) {
            RunCommand("useradd", "-M", "-s", "/sbin/nologin", request.Username);
            RunCommandWithInput("smbpasswd", $"{request.Password}\n{request.Password}\n", "-a", "-s", request.Username);
        } else {
            var path = GetUsersFilePath();
            File.AppendAllText(path, $"{request.Username}\n");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapDelete("/api/users/{username}", (string username) =>
{
    if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest();
    if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Root administrator account cannot be deleted." });

    try {
        var users = GetRegisteredUsers();
        users.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        SaveRegisteredUsers(users);

        if (OperatingSystem.IsLinux()) {
            RunCommand("smbpasswd", "-x", username);
            RunCommand("userdel", username);
        } else {
            var path = GetUsersFilePath();
            if (File.Exists(path)) {
                var lines = File.ReadAllLines(path).Where(l => l.Trim() != username).ToList();
                File.WriteAllLines(path, lines);
            }
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

app.MapPost("/api/users/{username}/password", async (HttpContext context, string username) =>
{
    var currentRole = context.Session.GetString("role") ?? "Viewer";
    if (currentRole != "Admin")
    {
        context.Response.StatusCode = 403;
        return Results.Json(new { error = "Forbidden: Administrator role required to reset user passwords." }, statusCode: 403);
    }

    if (string.IsNullOrWhiteSpace(username)) return Results.BadRequest();
    var req = await context.Request.ReadFromJsonAsync<AdminResetPasswordRequest>();
    if (req == null || string.IsNullOrWhiteSpace(req.NewPassword))
        return Results.BadRequest(new { error = "New password cannot be empty." });

    try {
        var users = GetRegisteredUsers();
        var user = users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (user == null)
            return Results.NotFound(new { error = $"User '{username}' not found." });

        var newHash = HashPassword(req.NewPassword);
        users.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        users.Add(user with { PasswordHash = newHash });
        SaveRegisteredUsers(users);

        if (username.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            var credFile = GetCredentialsFilePath();
            var dir = Path.GetDirectoryName(credFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(credFile, $"USERNAME={username}\nPASSWORD_HASH={newHash}\n");
            if (OperatingSystem.IsLinux()) RunCommand("chmod", "600", credFile);
        }

        if (OperatingSystem.IsLinux()) {
            RunCommandWithInput("smbpasswd", $"{req.NewPassword}\n{req.NewPassword}\n", "-s", username);
        }

        return Results.Ok(new { success = true, message = $"Password for '{username}' updated successfully." });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// SSL & Let's Encrypt APIs
app.MapGet("/api/ssl/status", () => Results.Ok(SslManager.GetStatus()));

app.MapPost("/api/ssl/self-signed", (SelfSignedRequest req) =>
{
    var domain = string.IsNullOrWhiteSpace(req?.Domain) ? "localhost" : req.Domain.Trim();
    var (success, message) = SslManager.GenerateSelfSignedCertificate(domain);
    if (!success) return Results.BadRequest(new { error = message });
    return Results.Ok(new { success = true, message });
});

app.MapPost("/api/ssl/letsencrypt/request", async (LetsEncryptRequest req) =>
{
    if (req == null || string.IsNullOrWhiteSpace(req.Domain) || string.IsNullOrWhiteSpace(req.Email))
        return Results.BadRequest(new { error = "Domain and email are required." });

    var (success, message) = await SslManager.RequestLetsEncryptCertificate(req.Domain, req.Email, req.Staging);
    if (!success) return Results.BadRequest(new { error = message });
    return Results.Ok(new { success = true, message });
});

// Automated Snapshot Scheduling APIs
app.MapGet("/api/snapshots/schedule", () => Results.Ok(SnapshotSchedulerService.GetSchedule()));

app.MapPost("/api/snapshots/schedule", (SnapshotSchedule schedule) =>
{
    if (schedule == null) return Results.BadRequest();
    SnapshotSchedulerService.SaveSchedule(schedule);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/snapshots/schedule/run", async () =>
{
    var schedule = SnapshotSchedulerService.GetSchedule();
    await SnapshotSchedulerService.RunSnapshotTaskAsync(schedule);
    return Results.Ok(new { success = true });
});

// Plugin & Extension Center APIs
app.MapGet("/api/plugins", () =>
{
    var plugins = PluginManager.GetAllPlugins();
    return Results.Ok(new { plugins });
});

app.MapPost("/api/plugins/install", (PluginInstallRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Plugin ID required." });
    var (success, message) = PluginManager.InstallPlugin(req.Id);
    return success ? Results.Ok(new { success = true, message }) : Results.BadRequest(new { error = message });
});

app.MapPost("/api/plugins/uninstall", (PluginInstallRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Plugin ID required." });
    var (success, message) = PluginManager.UninstallPlugin(req.Id);
    return success ? Results.Ok(new { success = true, message }) : Results.BadRequest(new { error = message });
});

app.MapPost("/api/plugins/toggle", (PluginToggleRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Plugin ID required." });
    var (success, message) = PluginManager.TogglePlugin(req.Id, req.Enabled);
    return success ? Results.Ok(new { success = true, message }) : Results.BadRequest(new { error = message });
});

app.MapPost("/api/plugins/config", (PluginConfigRequest req) =>
{
    if (string.IsNullOrWhiteSpace(req.Id)) return Results.BadRequest(new { error = "Plugin ID required." });
    var (success, message) = PluginManager.UpdateSettings(req.Id, req.Port, req.Settings ?? new());
    return success ? Results.Ok(new { success = true, message }) : Results.BadRequest(new { error = message });
});

// ZFS Pool Import APIs
app.MapGet("/api/zfs/importable", () =>
{
    try {
        if (OperatingSystem.IsLinux()) {
            var output = RunCommand("zpool", "import");
            if (string.IsNullOrWhiteSpace(output) || output.Contains("no pools available"))
                return Results.Ok(new { pools = Array.Empty<object>() });

            var pools = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("pool:"))
                .Select(line => line.Substring(5).Trim())
                .Select(name => new { name })
                .ToList();
            return Results.Ok(new { pools });
        } else {
            // Mock importable pools for dev mode
            var pools = new[] { new { name = "tank_backup" } };
            return Results.Ok(new { pools });
        }
    } catch {
        return Results.Ok(new { pools = Array.Empty<object>() });
    }
});

app.MapPost("/api/zfs/pools/import", async (HttpContext context) =>
{
    var request = await context.Request.ReadFromJsonAsync<ImportPoolRequest>();
    if (request == null || string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest();

    try {
        if (OperatingSystem.IsLinux()) {
            RunCommand("zpool", "import", request.Name);
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Problem(ex.Message);
    }
});

// Background ZFS Pool Health Monitor & Webhook Notifier
var webhookUrl = builder.Configuration["NotificationSettings:DiscordWebhookUrl"];
if (!string.IsNullOrWhiteSpace(webhookUrl))
{
    _ = Task.Run(async () => {
        var lastStates = new Dictionary<string, string>();
        var lastNotified = new Dictionary<string, DateTime>();
        using var client = new HttpClient();
        
        while (true)
        {
            try {
                if (OperatingSystem.IsLinux()) {
                    var output = RunCommand("zpool", "list", "-H");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var fields = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                            if (fields.Length >= 10)
                            {
                                var name = fields[0];
                                var health = fields[9];
                                
                                bool isUnhealthy = health != "ONLINE";
                                bool stateChanged = !lastStates.TryGetValue(name, out var prevHealth) || prevHealth != health;
                                
                                lastStates[name] = health;
                                
                                if (isUnhealthy)
                                {
                                    bool shouldNotify = stateChanged || 
                                                        !lastNotified.TryGetValue(name, out var lastTime) || 
                                                        (DateTime.UtcNow - lastTime).TotalHours >= 12;
                                                        
                                    if (shouldNotify)
                                    {
                                        lastNotified[name] = DateTime.UtcNow;
                                        var messagePayload = new {
                                            content = $"⚠️ **[SimpleNAS Alert]**: ZFS pool '**{name}**' is reporting **{health}** status! Immediate attention may be required."
                                        };
                                        
                                        var json = JsonSerializer.Serialize(messagePayload);
                                        using var content = new StringContent(json, Encoding.UTF8, "application/json");
                                        await client.PostAsync(webhookUrl, content);
                                    }
                                }
                            }
                        }
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[HealthMonitor Error]: {ex.Message}");
            }
            
            await Task.Delay(TimeSpan.FromMinutes(15));
        }
    });
}
// Cloud Storage Manager
bool isSyncActive = false;

app.MapGet("/api/cloud/status", () =>
{
    if (!OperatingSystem.IsLinux())
    {
        return Results.Json(new {
            rcloneMounted = false,
            rclonePath = "/mnt/gdrive",
            mergerfsMounted = false,
            mergerfsPath = "/mnt/tank/unified",
            syncActive = isSyncActive
        });
    }

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
        if (OperatingSystem.IsLinux()) {
            if (!Directory.Exists("/mnt/gdrive")) Directory.CreateDirectory("/mnt/gdrive");
            
            Task.Run(() => {
                RunCommand("rclone", "mount", "gdrive:", "/mnt/gdrive", "--vfs-cache-mode", "writes", "--allow-other", "--daemon");
            });
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/unmount", () =>
{
    try {
        if (OperatingSystem.IsLinux()) {
            RunCommand("fusermount", "-u", "/mnt/gdrive");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/union", () =>
{
    try {
        if (OperatingSystem.IsLinux()) {
            if (!Directory.Exists("/mnt/tank/unified")) Directory.CreateDirectory("/mnt/tank/unified");
            
            Task.Run(() => {
                RunCommand("mergerfs", "-o", "defaults,allow_other,use_ino,category.create=ff", "/mnt/tank/local:/mnt/gdrive", "/mnt/tank/unified");
            });
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/unmount-union", () =>
{
    try {
        if (OperatingSystem.IsLinux()) {
            RunCommand("fusermount", "-u", "/mnt/tank/unified");
        }
        return Results.Ok(new { success = true });
    } catch (Exception ex) {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/api/cloud/sync", () =>
{
    if (isSyncActive) return Results.Conflict(new { error = "Sync task is already running." });
    
    isSyncActive = true;
    Task.Run(async () => {
        try {
            if (OperatingSystem.IsLinux()) {
                RunCommand("rclone", "move", "/mnt/tank/local/Backups", "gdrive:Backups", "--min-age", "30d");
            } else {
                await Task.Delay(2000); // Simulate background sync in dev
            }
        } finally {
            isSyncActive = false;
        }
    });
    return Results.Ok(new { success = true });
});

Console.WriteLine("SimpleNAS running on http://0.0.0.0:8000");
Console.WriteLine("Default login: admin / SimpleNAS2026");
app.Run();

string GetUsersFilePath()
{
    return Path.Combine(Directory.GetCurrentDirectory(), "users.txt");
}

string GetDevicesFilePath() => Path.Combine(Directory.GetCurrentDirectory(), "mock_devices.txt");
string GetPoolsFilePath() => Path.Combine(Directory.GetCurrentDirectory(), "mock_pools.txt");
string GetDatasetsFilePath() => Path.Combine(Directory.GetCurrentDirectory(), "mock_datasets.txt");
string GetSnapshotsFilePath() => Path.Combine(Directory.GetCurrentDirectory(), "mock_snapshots.txt");

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
            
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
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
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
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
record NfsExport(string Path, List<string> Clients);
record ServiceControlRequest(string Service, string Action);
record FirewallToggleRequest(bool Enable);
record WhitelistRequest(string Ip, int Port, string Comment);
record CreateUserRequest(string Username, string Password, string? Role);
record NasUserRecord(string Username, string PasswordHash, string Role);
record SelfSignedRequest(string? Domain);
record LetsEncryptRequest(string Domain, string Email, bool Staging);
record ImportPoolRequest(string Name);
record CreateDatasetRequest(string Pool, string Name);
record CreateSnapshotRequest(string Dataset, string Name);
record RollbackSnapshotRequest(string Snapshot);
record ScrubPoolRequest(string Pool);
record PluginInstallRequest(string Id);
record PluginToggleRequest(string Id, bool Enabled);
record PluginConfigRequest(string Id, int? Port, Dictionary<string, string>? Settings);
record AdminResetPasswordRequest(string NewPassword);

