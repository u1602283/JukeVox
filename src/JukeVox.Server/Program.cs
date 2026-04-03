using System.Security.Cryptography.X509Certificates;
using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddJukeVoxServices(builder.Configuration);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:5173";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(frontendUrl)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Auto-detect mkcert PEM files at project root for TLS in local dev
var certPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../cert.pem"));
var keyPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../key.pem"));
if (File.Exists(certPath) && File.Exists(keyPath))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(5001,
            listenOptions => listenOptions.UseHttps(
                X509Certificate2.CreateFromPemFile(certPath, keyPath)));
    });
}

var app = builder.Build();

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors();
app.UseMiddleware<PartySessionMiddleware>(app.Services.GetRequiredService<IDataProtectionProvider>());
app.UseMiddleware<PartyContextMiddleware>();
app.UseMiddleware<HostActivityMiddleware>();

app.UseStaticFiles();
app.MapControllers();
app.MapHub<PartyHub>("/hubs/party");

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback: serve index.html for non-API, non-file routes
app.MapFallbackToFile("index.html");

app.Run();
