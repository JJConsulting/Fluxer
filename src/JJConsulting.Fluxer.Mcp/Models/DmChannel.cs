namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmChannel
{
    public required string Id { get; init; }
    public required string OtherUserId { get; init; }
    public string? Username { get; init; }
    public string? DisplayName { get; init; }
    public DateTimeOffset? LastMessageAt { get; init; }
}
