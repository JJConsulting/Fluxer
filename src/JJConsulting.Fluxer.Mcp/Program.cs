using JJConsulting.Fluxer.Client.Configuration;
using JJConsulting.Fluxer.Mcp.Authentication;
using JJConsulting.Fluxer.Mcp.Tools;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Request diagnostics can include OAuth credentials and private message text.
builder.Logging.AddFilter((category, level) =>
    category == "JJConsulting.Fluxer.Mcp" && level >= LogLevel.Information);
builder.Services.AddProblemDetails();

var configuration = builder.Configuration.GetSection("Fluxer");
var fluxer = configuration.Get<FluxerOptions>() ?? new FluxerOptions();
fluxer.Validate();

builder.Services.AddSingleton(fluxer);
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 1000;
});
builder.Services.AddSingleton<OAuthBridge>();
builder.Services.AddFluxerClient(configuration);
builder.Services.AddHttpClient(FluxerOptions.Scheme, client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.MaxResponseContentBufferSize = 65536;
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false, UseCookies = false
}).RemoveAllLoggers();
builder.Services.AddAuthentication(FluxerOptions.Scheme)
    .AddScheme<AuthenticationSchemeOptions, FluxerAuthenticationHandler>(FluxerOptions.Scheme, _ => { });
builder.Services.AddHttpContextAccessor();
builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true)
    .WithTools<IdentityTools>().WithTools<DmTools>();

var app = builder.Build();
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
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next(context);
});
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/mcp") && context.User.Identity?.IsAuthenticated != true)
    {
        await context.ChallengeAsync();
        return;
    }

    await next(context);
});
app.MapGet("/.well-known/oauth-protected-resource", (HttpRequest request) => Results.Json(new
{
    resource = fluxer.GetPublicUrl(request) + "/mcp", authorization_servers = new[] { fluxer.GetPublicUrl(request) },
    scopes_supported = new[] { FluxerOptions.Scope }, bearer_methods_supported = new[] { "header" }
}));
app.MapGet("/.well-known/oauth-protected-resource/mcp",
    () => Results.Redirect("/.well-known/oauth-protected-resource"));
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
app.MapMcp("/mcp");
app.Run();

public partial class Program;