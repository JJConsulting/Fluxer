namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmMessage(
    string Id,
    string AuthorId,
    string Text,
    DateTimeOffset Timestamp,
    string? AuthorName = null);