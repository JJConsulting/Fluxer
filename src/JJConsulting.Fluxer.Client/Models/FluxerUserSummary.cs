namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerUserSummary
{
    public required string Id { get; init; }
    public required string Username { get; init; }
    public string? GlobalName { get; init; }
}
