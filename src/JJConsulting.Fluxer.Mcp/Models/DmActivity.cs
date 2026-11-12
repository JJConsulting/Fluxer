namespace JJConsulting.Fluxer.Mcp.Models;

public sealed class DmActivity
{
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public required IReadOnlyList<DmConversationActivity> Conversations { get; init; }
    public required int ScannedChannels { get; init; }
    public string? NextChannelBefore { get; init; }
}
