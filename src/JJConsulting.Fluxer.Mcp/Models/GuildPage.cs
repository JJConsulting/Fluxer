namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class GuildPage
{
    public required IReadOnlyList<GuildSummary> Guilds { get; init; }
    public string? NextAfter { get; init; }
}
