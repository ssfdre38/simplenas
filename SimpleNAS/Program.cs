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
    return Results.Json(new {
        cpu = new { percent = 0.0 },
        memory = new { percent = 0.0 },
        disk = new { percent = diskPercent }
    });
});

Console.WriteLine("SimpleNAS running on http://0.0.0.0:8000");
Console.WriteLine("Default login: admin / SimpleNAS2026");
app.Run();

record ZfsPoolRequest(string Name, string VdevType, string[] Devices);
record LoginRequest(string Username, string Password);
record ChangePasswordRequest(string CurrentPassword, string NewPassword);
