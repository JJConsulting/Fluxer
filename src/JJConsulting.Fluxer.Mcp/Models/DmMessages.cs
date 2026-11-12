namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmMessages(IReadOnlyList<DmMessage> Messages, bool HasMore, string? NextBefore);