namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmChannel(
    string Id,
    string OtherUserId,
    string? Username = null,
    string? DisplayName = null,
    DateTimeOffset? LastMessageAt = null);