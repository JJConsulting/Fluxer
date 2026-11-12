using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JJConsulting.Fluxer.Client;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFluxerClient(IConfigurationSection configuration)
        {
            services.AddOptions<FluxerClientOptions>().Bind(configuration)
                .Validate(options => options.IsValid(), "Fluxer client settings are missing or invalid.")
                .ValidateOnStart();

            services.AddHttpClient<IFluxerAdminClient, FluxerAdminClient>(client =>
                    client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                })
                .RemoveAllLoggers();

            services.AddHttpClient<IFluxerIdentityClient, FluxerIdentityClient>(client =>
                    client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                })
                .RemoveAllLoggers();

            return services;
        }
    }
}
