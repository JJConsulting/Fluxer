namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerGuildPage
{
    public required IReadOnlyList<FluxerGuildSummary> Guilds { get; init; }
}
