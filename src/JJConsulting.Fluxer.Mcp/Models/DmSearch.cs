namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmSearch(string ChannelId, IReadOnlyList<DmMessage> Messages, int Total, bool Truncated);