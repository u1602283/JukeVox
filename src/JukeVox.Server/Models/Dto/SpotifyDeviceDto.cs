namespace JukeVox.Server.Models.Dto;

public class SpotifyDeviceDto
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public bool IsActive { get; set; }
    public int VolumePercent { get; set; }
    public bool SupportsVolume { get; set; } = true;
}
