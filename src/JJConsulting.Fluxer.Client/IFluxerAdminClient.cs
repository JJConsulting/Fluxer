using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Client;

// Server-side primitives only: the caller must authorize the represented user and channel.
public interface IFluxerAdminClient
{
    Task<FluxerResult<FluxerDmPage>> GetIndividualDmPageAsync(string userId, FluxerPageRequest? page = null,
        CancellationToken cancellationToken = default);

    // Returns only after pagination completes. Failures never produce a partial membership list.
    Task<FluxerResult<IReadOnlyList<FluxerDmChannel>>> GetAllIndividualDmsAsync(string userId,
        CancellationToken cancellationToken = default);

    Task<FluxerResult<FluxerMessagePage>> GetMessagesAsync(string channelId, FluxerPageRequest? page = null,
        CancellationToken cancellationToken = default);
    Task<FluxerResult<FluxerMessageSearch>> SearchMessagesAsync(string channelId, string query, int limit = 25,
        CancellationToken cancellationToken = default);
}