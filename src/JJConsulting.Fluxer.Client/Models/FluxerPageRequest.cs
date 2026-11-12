namespace JJConsulting.Fluxer.Client.Models;

// These APIs are server-side primitives, not employee authorization or MCP tools.

public sealed class FluxerPageRequest(int limit = 50, string? before = null, string? after = null)
{
    public int Limit { get; init; } = limit;
    public string? Before { get; init; } = before;
    public string? After { get; init; } = after;
}