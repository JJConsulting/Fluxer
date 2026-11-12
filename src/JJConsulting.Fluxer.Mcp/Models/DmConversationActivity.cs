namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmConversationActivity
{
    public required DmChannel Channel { get; init; }
    public required DmMessages Messages { get; init; }
}
