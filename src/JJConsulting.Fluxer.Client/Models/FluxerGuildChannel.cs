namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerGuildChannel
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int? Type { get; init; }
    public bool? Nsfw { get; init; }
}
