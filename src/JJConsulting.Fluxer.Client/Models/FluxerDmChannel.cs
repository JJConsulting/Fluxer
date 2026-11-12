namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerDmChannel
{
    public required string ChannelId { get; init; }
    public required int? ChannelType { get; init; }
    public bool? ChannelNsfw { get; init; }
    public required IReadOnlyList<string> RecipientIds { get; init; }
    public IReadOnlyList<FluxerUserSummary> Recipients { get; init; } = [];
    public string? LastMessageId { get; init; }
}