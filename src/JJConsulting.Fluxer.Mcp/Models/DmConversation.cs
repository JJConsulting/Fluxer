namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmConversation(DmChannel Channel, DmMessage? LatestMessage);