namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerGuild
{
    public required string Id { get; init; }
    public required string OwnerId { get; init; }
    public bool? Nsfw { get; init; }
    public required IReadOnlyList<FluxerGuildChannel> Channels { get; init; }
}
