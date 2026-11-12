using JJConsulting.Fluxer.Mcp.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Configuration;

internal static class WebApplicationExtensions
{
    public static WebApplication UseFluxerMcp(this WebApplication app)
    {
        var fluxer = app.Services.GetRequiredService<IOptions<FluxerOptions>>().Value;

        app.UseExceptionHandler();
        app.Use(async (context, next) =>
        {
            if (string.IsNullOrEmpty(fluxer.PublicUrl) && !context.Request.IsHttps)
            {
                context.Response.StatusCode = 400;
                return;
            }

            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            await next(context);
        });
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/" && context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }

            await next(context);
        });

        app.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) => Results.Json(new
        {
            resource = fluxer.GetPublicUrl(request), authorization_servers = new[] { fluxer.GetPublicUrl(request) },
            scopes_supported = new[] { FluxerOptions.Scope }, bearer_methods_supported = new[] { "header" }
        }));
        app.MapGet("/.well-known/oauth-authorization-server", (HttpRequest request) => Results.Json(new
        {
            issuer = fluxer.GetPublicUrl(request),
            authorization_response_iss_parameter_supported = true,
            authorization_endpoint = fluxer.GetPublicUrl(request) + "/oauth/authorize",
            token_endpoint = fluxer.GetPublicUrl(request) + "/oauth/token",
            revocation_endpoint = fluxer.GetPublicUrl(request) + "/oauth/revoke",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none" },
            scopes_supported = new[] { FluxerOptions.Scope }
        }));
        app.MapGet("/oauth/authorize", (HttpContext context, OAuthBridge bridge) => bridge.Authorize(context));
        app.MapGet(OAuthBridge.CallbackPath, (HttpContext context, OAuthBridge bridge) => bridge.Callback(context));
        app.MapPost("/oauth/token", (HttpContext context, OAuthBridge bridge) => bridge.TokenAsync(context));
        app.MapPost("/oauth/revoke", (HttpContext context, OAuthBridge bridge) => bridge.RevokeAsync(context));
        app.MapGet("/health", () => Results.Ok());
        app.MapMcp();

        return app;
    }
}
