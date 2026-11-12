using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Client;

public interface IFluxerIdentityClient
{
    Task<FluxerResult<FluxerIdentity>> GetIdentityAsync(string accessToken, CancellationToken cancellationToken = default);
}