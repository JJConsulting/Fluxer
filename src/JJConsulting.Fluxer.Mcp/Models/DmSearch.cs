namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmSearch
{
    public required string ChannelId { get; init; }
    public required IReadOnlyList<DmMessage> Messages { get; init; }
    public required int Total { get; init; }
    public required bool Truncated { get; init; }
}
