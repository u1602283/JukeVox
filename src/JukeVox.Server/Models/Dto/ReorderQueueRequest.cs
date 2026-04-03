namespace JukeVox.Server.Models.Dto;

public class ReorderQueueRequest
{
    public required List<string> OrderedIds { get; set; }
}
