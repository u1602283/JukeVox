namespace JukeVox.Server.Models;

public class HostCredential
{
    public required byte[] CredentialId { get; set; }
    public required byte[] PublicKey { get; set; }
    public uint SignCount { get; set; }
}
