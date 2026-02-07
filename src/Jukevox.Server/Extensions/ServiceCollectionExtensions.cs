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

        services.AddSingleton<PartyService>();
        services.AddSingleton<QueueService>();

        services.AddHttpClient<SpotifyAuthService>();
        services.AddHttpClient<SpotifySearchService>();
        services.AddHttpClient<SpotifyPlayerService>();

        services.AddSingleton<PlaybackMonitorService>();
        services.AddHostedService<PlaybackMonitorService>(sp => sp.GetRequiredService<PlaybackMonitorService>());

        return services;
    }
}
