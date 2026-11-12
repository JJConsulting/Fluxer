using System.Globalization;
using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client;

// Server-side primitives only: the caller must authorize the represented user and channel.
public sealed class FluxerAdminClient
{
    private readonly FluxerHttpTransport _transport;
    private readonly FluxerClientOptions _options;

    public FluxerAdminClient(HttpClient client, IOptions<FluxerClientOptions> options)
    {
        _options = options.Value;
        _transport = new FluxerHttpTransport(client, _options);
    }

    public async Task<FluxerResult<FluxerDmPage>> GetIndividualDmPageAsync(string userId,
        FluxerPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(userId);
        page ??= new FluxerPageRequest(_options.DmPageSize);
        var query = PageQuery(page, 200);
        var response = await _transport.GetAsync<FluxerDmPage>(
            $"v1/admin/users/{userId}/dm-channels?type=dm&{query}", "Admin", _options.AdminApiKey, cancellationToken);

        if (response is FluxerError error)
            return error;
        if (response is not FluxerDmPage result)
            return InvalidResponse();
        if (result.Channels is null || result.Channels.Count > page.Limit)
            return InvalidResponse();

        foreach (var channel in result.Channels)
        {
            if (channel is null || !IsId(channel.ChannelId) || channel.ChannelType != 1 ||
                channel.RecipientIds is null || channel.RecipientIds.Count > 100 ||
                channel.RecipientIds.Any(id => !IsId(id)) || channel.Recipients is null ||
                channel.Recipients.Count > 100 ||
                channel.Recipients.Select(r => r?.Id).Distinct(StringComparer.Ordinal).Count() !=
                channel.Recipients.Count ||
                channel.Recipients.Any(r =>
                    r is null || !IsId(r.Id) || r.Username is null || !channel.RecipientIds.Contains(r.Id)) ||
                channel.LastMessageId is not null && !IsId(channel.LastMessageId))
                return InvalidResponse();
        }

        if (!HasValidPageIds(result.Channels.Select(channel => channel.ChannelId), page))
            return InvalidResponse();
        return result;
    }

    // Returns only after pagination completes. Failures never produce a partial membership list.
    public async Task<FluxerResult<IReadOnlyList<FluxerDmChannel>>> GetAllIndividualDmsAsync(string userId,
        CancellationToken cancellationToken = default)
    {
        var channels = new List<FluxerDmChannel>();
        string? before = null;
        for (var index = 0; index < _options.MaxDmPages; index++)
        {
            var response = await GetIndividualDmPageAsync(userId,
                new FluxerPageRequest(_options.DmPageSize, Before: before), cancellationToken);
            if (response is FluxerError error)
                return error;
            if (response is not FluxerDmPage page)
                return InvalidResponse();
            if (page.Channels.Count == 0)
                return channels.AsReadOnly();

            channels.AddRange(page.Channels);
            // Do not assume array ordering or that a short page proves completion.
            before = page.Channels.MinBy(channel => ParseId(channel.ChannelId))!.ChannelId;
        }

        return new FluxerError(new FluxerLocalError(FluxerFailure.PaginationLimitExceeded));
    }

    public async Task<FluxerResult<FluxerMessagePage>> GetMessagesAsync(string channelId,
        FluxerPageRequest? page = null,
        CancellationToken cancellationToken = default)
    {
        ValidateId(channelId);
        page ??= new FluxerPageRequest();
        var query = PageQuery(page, 100);
        var response = await _transport.GetAsync<FluxerMessagePage>(
            $"v1/admin/channels/{channelId}/messages?{query}", "Admin", _options.AdminApiKey, cancellationToken);
        if (response is FluxerError error) return error;
        if (response is not FluxerMessagePage result ||
            result.Messages is null || result.Messages.Count > page.Limit ||
            result is { HasMore: true, Messages.Count: 0 }) return InvalidResponse();

        foreach (var message in result.Messages)
        {
            if (message is null || !IsId(message.Id) || message.ChannelId != channelId ||
                !IsId(message.AuthorId) || message.Content is null || message.Timestamp == default)
                return InvalidResponse();
        }

        if (!HasValidPageIds(result.Messages.Select(message => message.Id), page))
            return InvalidResponse();
        return result;
    }

    public async Task<FluxerResult<FluxerMessageSearch>> SearchMessagesAsync(string channelId, string query,
        int limit = 25, CancellationToken cancellationToken = default)
    {
        ValidateId(channelId);
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
            throw new ArgumentException("Search text must contain 1-200 characters.", nameof(query));
        var paging = PageQuery(new FluxerPageRequest(limit), 100);
        var response = await _transport.GetAsync<FluxerMessageSearch>(
            $"v1/admin/messages?channel_id={channelId}&q={Uri.EscapeDataString(query)}&{paging}",
            "Admin", _options.AdminApiKey, cancellationToken);
        if (response is FluxerError error) return error;
        if (response is not FluxerMessageSearch result || result.Messages is null ||
            result.Messages.Count > limit || result.Total < result.Messages.Count ||
            result.Messages.Any(message => message is null || !IsId(message.Id) || message.ChannelId != channelId ||
                                           !IsId(message.AuthorId) || message.Content is null ||
                                           message.Timestamp == default) ||
            result.Messages.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != result.Messages.Count)
            return InvalidResponse();
        return result;
    }

    private static string PageQuery(FluxerPageRequest page, int maximum)
    {
        if (page.Limit < 1 || page.Limit > maximum || (page.Before is not null && page.After is not null))
            throw new ArgumentException("Invalid Fluxer pagination.", nameof(page));
        if (page.Before is not null) ValidateId(page.Before, allowZero: true);
        if (page.After is not null) ValidateId(page.After, allowZero: true);
        return $"limit={page.Limit.ToString(CultureInfo.InvariantCulture)}" +
               (page.Before is null ? "" : $"&before={page.Before}") +
               (page.After is null ? "" : $"&after={page.After}");
    }

    private static bool HasValidPageIds(IEnumerable<string> ids, FluxerPageRequest page)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!seen.Add(id) || (page.Before is not null && ParseId(id) >= ParseId(page.Before)) ||
                (page.After is not null && ParseId(id) <= ParseId(page.After)))
                return false;
        }

        return true;
    }

    internal static bool IsId(string? id)
    {
        return id is { Length: > 0 and <= 19 } &&
               id[0] != '0' && id.All(c => c is >= '0' and <= '9') &&
               long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static long ParseId(string id)
    {
        return long.Parse(id, CultureInfo.InvariantCulture);
    }

    private static void ValidateId(string id, bool allowZero = false)
    {
        if (!IsId(id) && !(allowZero && id == "0"))
            throw new ArgumentException("Invalid Fluxer ID.", nameof(id));
    }

    private static FluxerError InvalidResponse()
    {
        return new FluxerError(new FluxerLocalError(FluxerFailure.InvalidResponse));
    }
}