using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class GuildMessages
{
    public required string GuildId { get; init; }
    public required string ChannelId { get; init; }
    public required IReadOnlyList<FluxerMessage> Messages { get; init; }
    public required bool HasMore { get; init; }
    public string? NextBefore { get; init; }
}
