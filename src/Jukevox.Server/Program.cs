using JukeVox.Server.Extensions;
using JukeVox.Server.Hubs;
using JukeVox.Server.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddJukeVoxServices(builder.Configuration);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors();
app.UseMiddleware<PartySessionMiddleware>();

app.MapControllers();
app.MapHub<PartyHub>("/hubs/party");

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
