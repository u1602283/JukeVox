namespace JukeVox.Server.Configuration;

public class PartyInactivityOptions
{
    public const string SectionName = "PartyInactivity";
    public int SleepAfterMinutes { get; set; } = 15;
    public int AutoEndAfterMinutes { get; set; } = 120;
}
