namespace JukeVox.Server.Models;

public class HostCredential
{
    public required string HostId { get; set; }
    public required string DisplayName { get; set; }
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public uint SignCount { get; set; }
    public bool IsAdmin { get; set; }
}
