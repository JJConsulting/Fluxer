namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmMessage
{
    public required string Id { get; init; }
    public required string AuthorId { get; init; }
    public required string Text { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? AuthorName { get; init; }
}
