namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerMessageSearch
{
    public required IReadOnlyList<FluxerMessage> Messages { get; init; }
    public required int Total { get; init; }
}
