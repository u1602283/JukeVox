namespace JukeVox.Server.Models.Dto;

public class PlaybackStateDto
{
    public bool IsPlaying { get; set; }
    public string? TrackUri { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public string? AlbumImageUrl { get; set; }
    public int ProgressMs { get; set; }
    public int DurationMs { get; set; }
    public int VolumePercent { get; set; }
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
}
