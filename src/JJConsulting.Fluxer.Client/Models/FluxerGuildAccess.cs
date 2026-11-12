namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerGuildAccess
{
    // A null cutoff grants the full history; absent channels cannot be read.
    public required IReadOnlyDictionary<string, DateTimeOffset?> ReadableChannels { get; init; }
}
