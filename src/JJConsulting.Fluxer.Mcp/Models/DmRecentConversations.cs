namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmRecentConversations(IReadOnlyList<DmConversation> Conversations, int Total);