namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerMessage
{
    public required string Id { get; init; }
    public required string ChannelId { get; init; }
    public required string AuthorId { get; init; }
    public string? AuthorUsername { get; init; }
    public string? AuthorGlobalName { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}