namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmChannels(IReadOnlyList<DmChannel> Channels, string? NextBefore);