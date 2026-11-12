using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Authentication;

public sealed class FluxerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHttpClientFactory clients,
    IOptions<FluxerOptions> options) : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    private readonly FluxerOptions _fluxerOptions = options.Value;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization")) 
            return AuthenticateResult.NoResult();
        
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header))
            return AuthenticateResult.Fail("Invalid bearer token.");

        if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.Fail("Invalid bearer token.");

        if (string.IsNullOrEmpty(header.Parameter) || !header.Parameter.All(c => c is >= '!' and <= '~'))
            return AuthenticateResult.Fail("Invalid bearer token.");

        try
        {
            using var client = clients.CreateClient(FluxerOptions.Scheme);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = header.Parameter, ["client_id"] = _fluxerOptions.ClientId,
                ["client_secret"] = _fluxerOptions.ClientSecret
            });
            using var response = await client.PostAsync(_fluxerOptions.ApiBaseUrl + "/v1/oauth2/introspect", form,
                Context.RequestAborted);
            if (!response.IsSuccessStatusCode)
                return AuthenticateResult.Fail("Fluxer authentication unavailable.");
            var token = JsonSerializer.Deserialize<FluxerTokenResponse>(await response.Content.ReadAsStringAsync(Context.RequestAborted));
            // Use an OAuth application dedicated to this MCP: its client_id binds tokens to this service.
            if (token is null || !token.Active)
                return AuthenticateResult.Fail("Invalid Fluxer access token.");

            if (token.ClientId != _fluxerOptions.ClientId || token.TokenType != "Bearer")
                return AuthenticateResult.Fail("Invalid Fluxer access token.");

            if (!FluxerOptions.IsId(token.Subject))
                return AuthenticateResult.Fail("Invalid Fluxer access token.");

            if (token.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                return AuthenticateResult.Fail("Invalid Fluxer access token.");

            if (token.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(FluxerOptions.Scope) != true)
                return AuthenticateResult.Fail("Invalid Fluxer access token.");
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, token.Subject!), new Claim("scope", FluxerOptions.Scope)
            ], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
        {
            return AuthenticateResult.Fail("Fluxer authentication could not be verified.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{_fluxerOptions.GetPublicUrl(Request)}/.well-known/oauth-protected-resource\", scope=\"{FluxerOptions.Scope}\"";
        return Task.CompletedTask;
    }
}
