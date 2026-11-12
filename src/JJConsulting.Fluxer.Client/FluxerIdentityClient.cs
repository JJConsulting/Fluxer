using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client;

public sealed class FluxerIdentityClient(HttpClient client, IOptions<FluxerClientOptions> options)
{
    private readonly FluxerHttpTransport _transport = new(client, options.Value);

    public async Task<FluxerResult<FluxerIdentity>> GetIdentityAsync(string accessToken,
        CancellationToken cancellationToken = default)
    {
        var response = await _transport.GetAsync<FluxerIdentity>("v1/oauth2/userinfo", "Bearer", accessToken,
            cancellationToken);

        if (response is FluxerError error)
            return error;
        if (response is not FluxerIdentity identity || !FluxerAdminClient.IsId(identity.Id) || identity.Sub != identity.Id)
            return new FluxerError(new FluxerLocalError(FluxerFailure.InvalidResponse));

        return identity;
    }
}
