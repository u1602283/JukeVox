using System.ComponentModel.DataAnnotations;

namespace Jukevox.Server.Configuration;

public class SpotifyOptions
{
    public const string SectionName = "Spotify";

    [Required]
    public required string ClientId { get; set; }

    [Required]
    public required string ClientSecret { get; set; }

    [Required]
    public required string RedirectUri { get; set; }
}
