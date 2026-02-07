namespace JukeVox.Server.Models.Dto;

public class SpotifyPlaylistDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? ImageUrl { get; set; }
    public int TrackCount { get; set; }
}
