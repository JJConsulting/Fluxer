namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmChannels
{
    public required IReadOnlyList<DmChannel> Channels { get; init; }
    public string? NextBefore { get; init; }
}
