namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerMessagePage
{
    public required IReadOnlyList<FluxerMessage> Messages { get; init; }
    public required bool HasMore { get; init; }
}