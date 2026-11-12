namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmContacts
{
    public required IReadOnlyList<DmChannel> Contacts { get; init; }
    public required int Total { get; init; }
    public required bool Truncated { get; init; }
}
