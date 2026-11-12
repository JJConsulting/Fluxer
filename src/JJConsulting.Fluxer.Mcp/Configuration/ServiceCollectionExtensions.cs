using JJConsulting.Fluxer.Client.Configuration;
using JJConsulting.Fluxer.Mcp.Authentication;
using JJConsulting.Fluxer.Mcp.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Configuration;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFluxerMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddProblemDetails();

        var section = configuration.GetSection("Fluxer");
        services.AddSingleton<IValidateOptions<FluxerOptions>, FluxerOptionsValidator>();
        services.AddOptions<FluxerOptions>().Bind(section).ValidateOnStart();
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1000;
        });
        services.AddSingleton<OAuthBridge>();
        services.AddFluxerClient(section);
        services.AddHttpClient(FluxerOptions.Scheme, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.MaxResponseContentBufferSize = 65536;
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseCookies = false
        }).RemoveAllLoggers();
        services.AddAuthentication(FluxerOptions.Scheme)
            .AddScheme<AuthenticationSchemeOptions, FluxerAuthenticationHandler>(FluxerOptions.Scheme, _ => { });
        services.AddHttpContextAccessor();
        services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
            .WithTools<IdentityTools>().WithTools<DmTools>();

        return services;
    }
}
