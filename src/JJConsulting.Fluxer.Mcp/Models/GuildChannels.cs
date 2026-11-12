namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class GuildChannels
{
    public required string GuildId { get; init; }
    public required IReadOnlyList<GuildChannel> Channels { get; init; }
}
