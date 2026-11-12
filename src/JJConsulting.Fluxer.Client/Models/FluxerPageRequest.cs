namespace JJConsulting.Fluxer.Client.Models;

// These APIs are server-side primitives, not employee authorization or MCP tools.

public sealed record FluxerPageRequest(int Limit = 50, string? Before = null, string? After = null);