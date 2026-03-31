using Fido2NetLib;
using Microsoft.AspNetCore.DataProtection;
using JukeVox.Server.Configuration;
using JukeVox.Server.Services;

namespace JukeVox.Server.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJukeVoxServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SpotifyOptions>()
            .Bind(configuration.GetSection(SpotifyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPartyService, PartyService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddSingleton<ConnectionMapping>();
        services.AddScoped<IPartyContextAccessor, PartyContextAccessor>();

        services.AddHttpClient<ISpotifyAuthService, SpotifyAuthService>();
        services.AddHttpClient<ISpotifySearchService, SpotifySearchService>();
        services.AddHttpClient<ISpotifyPlayerService, SpotifyPlayerService>();
        services.AddHttpClient<ISpotifyPlaylistService, SpotifyPlaylistService>();

        services.AddSingleton<PlaybackMonitorService>();
        services.AddSingleton<IPlaybackMonitorService>(sp => sp.GetRequiredService<PlaybackMonitorService>());
        services.AddHostedService<PlaybackMonitorService>(sp => sp.GetRequiredService<PlaybackMonitorService>());

        // Host authentication (Fido2 config must be registered before HostCredentialService)
        var serverDomain = configuration["HostAuth:ServerDomain"] ?? "localhost";
        var serverName = serverDomain.Split('.')[0];

        var fido2Config = new Fido2Configuration
        {
            ServerDomain = serverDomain,
            ServerName = serverName,
            Origins = new HashSet<string>(
                (configuration["HostAuth:Origins"] ?? "http://localhost:5173")
                    .Split(';', StringSplitOptions.RemoveEmptyEntries))
        };
        services.AddSingleton(fido2Config);
        services.AddSingleton<Fido2>();
        services.AddSingleton<HostCredentialService>();

        // Ephemeral keys so host auth cookies are invalidated on server restart
        services.AddDataProtection().UseEphemeralDataProtectionProvider();

        return services;
    }
}
