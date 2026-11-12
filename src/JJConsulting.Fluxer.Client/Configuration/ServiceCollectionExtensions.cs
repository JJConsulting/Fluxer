using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client.Configuration;

public static class ServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddFluxerClient(IConfigurationSection configuration)
        {
            services.AddSingleton<IValidateOptions<FluxerClientOptions>, FluxerClientOptionsValidator>();
            services.AddOptions<FluxerClientOptions>().Bind(configuration)
                .ValidateOnStart();

            services.AddHttpClient<FluxerAdminClient>(client =>
                    client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                })
                .RemoveAllLoggers();

            services.AddHttpClient<FluxerIdentityClient>(client =>
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
