namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmConversation
{
    public required DmChannel Channel { get; init; }
    public DmMessage? LatestMessage { get; init; }
}
