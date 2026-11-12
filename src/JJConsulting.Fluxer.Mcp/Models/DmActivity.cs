namespace JJConsulting.Fluxer.Mcp.Models;

public sealed record DmActivity(
    DateTimeOffset Start,
    DateTimeOffset End,
    IReadOnlyList<DmConversationActivity> Conversations,
    int ScannedChannels,
    string? NextChannelBefore);