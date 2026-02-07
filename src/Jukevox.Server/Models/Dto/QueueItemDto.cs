namespace JukeVox.Server.Models.Dto;

public class QueueItemDto
{
    public required string Id { get; set; }
    public required string TrackUri { get; set; }
    public required string TrackName { get; set; }
    public required string ArtistName { get; set; }
    public required string AlbumName { get; set; }
    public string? AlbumImageUrl { get; set; }
    public int DurationMs { get; set; }
    public required string AddedByName { get; set; }
    public DateTime AddedAt { get; set; }
}
