namespace Jukevox.Server.Models.Dto;

public class SearchResultDto
{
    public required string TrackUri { get; set; }
    public required string TrackName { get; set; }
    public required string ArtistName { get; set; }
    public required string AlbumName { get; set; }
    public string? AlbumImageUrl { get; set; }
    public int DurationMs { get; set; }
}
