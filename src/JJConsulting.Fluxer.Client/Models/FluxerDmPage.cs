namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerDmPage
{
    public required IReadOnlyList<FluxerDmChannel> Channels { get; init; }
}