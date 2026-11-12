using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Authentication;

public sealed class FluxerAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemes,
    ILoggerFactory logger, 
    UrlEncoder encoder,
    IHttpClientFactory clients, FluxerOptions fluxer) : AuthenticationHandler<AuthenticationSchemeOptions>(schemes, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization")) return AuthenticateResult.NoResult();
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(header.Parameter) || !header.Parameter.All(c => c is >= '!' and <= '~'))
            return AuthenticateResult.Fail("Invalid bearer token.");
        try
        {
            using var client = clients.CreateClient(FluxerOptions.Scheme);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = header.Parameter, ["client_id"] = fluxer.ClientId,
                ["client_secret"] = fluxer.ClientSecret
            });
            using var response = await client.PostAsync(fluxer.ApiBaseUrl + "/v1/oauth2/introspect", form,
                Context.RequestAborted);
            if (!response.IsSuccessStatusCode) return AuthenticateResult.Fail("Fluxer authentication unavailable.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
            var token = document.RootElement;
            var subject = token.GetProperty("sub").GetString();
            // Use an OAuth application dedicated to this MCP: its client_id binds tokens to this service.
            if (!token.GetProperty("active").GetBoolean() ||
                token.GetProperty("client_id").GetString() != fluxer.ClientId ||
                token.GetProperty("token_type").GetString() != "Bearer" || !FluxerOptions.IsId(subject) ||
                token.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
                token.GetProperty("scope").GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(FluxerOptions.Scope) != true)
                return AuthenticateResult.Fail("Invalid Fluxer access token.");
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, subject!), new Claim("scope", FluxerOptions.Scope)
            ], Scheme.Name);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return AuthenticateResult.Fail("Fluxer authentication could not be verified.");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate =
            $"Bearer resource_metadata=\"{fluxer.GetPublicUrl(Request)}/.well-known/oauth-protected-resource\", scope=\"{FluxerOptions.Scope}\"";
        return Task.CompletedTask;
    }
}
