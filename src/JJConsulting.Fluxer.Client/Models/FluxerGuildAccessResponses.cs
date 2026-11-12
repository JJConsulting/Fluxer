namespace JJConsulting.Fluxer.Client.Models;

internal sealed class FluxerGuildAccessResponse
{
    public required string Id { get; init; }
    public required bool Nsfw { get; init; }
    public bool Visiting { get; init; }
    public required DateTimeOffset? MessageHistoryCutoff { get; init; }
    public required IReadOnlyList<FluxerGuildAccessRole> Roles { get; init; }
    public required IReadOnlyList<FluxerGuildAccessChannel> Channels { get; init; }
}

internal sealed class FluxerGuildAccessRole
{
    public required string Id { get; init; }
    public required string Permissions { get; init; }
}

internal sealed class FluxerGuildAccessChannel
{
    public required string Id { get; init; }
    public required IReadOnlyList<FluxerPermissionOverwrite> PermissionOverwrites { get; init; }
}

internal sealed class FluxerPermissionOverwrite
{
    public required string Id { get; init; }
    public required int Type { get; init; }
    public required string Allow { get; init; }
    public required string Deny { get; init; }
}

internal sealed class FluxerGuildMemberPage
{
    public required IReadOnlyList<FluxerGuildAccessMember> Members { get; init; }
}

internal sealed class FluxerGuildAccessMember
{
    public required FluxerGuildMemberUser User { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
}

internal sealed class FluxerGuildMemberUser
{
    public required string Id { get; init; }
}
