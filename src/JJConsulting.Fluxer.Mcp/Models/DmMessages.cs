namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmMessages
{
    public required IReadOnlyList<DmMessage> Messages { get; init; }
    public required bool HasMore { get; init; }
    public string? NextBefore { get; init; }
}
