namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmRecentConversations
{
    public required IReadOnlyList<DmConversation> Conversations { get; init; }
    public required int Total { get; init; }
}
