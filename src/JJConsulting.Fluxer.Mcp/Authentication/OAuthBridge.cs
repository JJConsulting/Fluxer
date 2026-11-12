using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Authentication;

// shortcut: pending logins live in one process; use shared state before running multiple replicas.
public sealed class OAuthBridge(IOptions<FluxerOptions> settings, IHttpClientFactory clients, IMemoryCache cache)
{
    private readonly FluxerOptions options = settings.Value;

    public const string CallbackPath = "/auth/fluxer/callback";

    private sealed record Login(string State, string Challenge, string Callback, string Correlation, string ClientRedirectUri)
    {
        public int Used;
    }

    private sealed record Code(Login Login, string UpstreamCode)
    {
        public int Used;
    }

    public IResult Authorize(HttpContext context)
    {
        var query = context.Request.Query;
        if (query.Any(p => p.Value.Count != 1) || query["client_id"] != options.ClientId ||
            !options.AllowedClientRedirectUris.Contains(query["redirect_uri"].ToString(), StringComparer.Ordinal))
            return Error("invalid_request");
        var state = query["state"].ToString();
        var challenge = query["code_challenge"].ToString();
        if (state.Length is < 1 or > 256 || query["response_type"] != "code")
            return Error("invalid_request");

        if (query["code_challenge_method"] != "S256" || challenge.Length != 43)
            return Error("invalid_request");

        if (!challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return Error("invalid_request");

        if (query["resource"] != options.GetPublicUrl(context.Request) + "/mcp")
            return Error("invalid_request");
        if (query["scope"] != FluxerOptions.Scope) return Error("invalid_scope");

        var key = Random();
        var login = new Login(state, challenge, options.GetPublicUrl(context.Request) + CallbackPath, Random(), query["redirect_uri"].ToString());
        if (!Store("login:" + key, login, TimeSpan.FromMinutes(10)))
            return Error("temporarily_unavailable", 503);
        context.Response.Cookies.Append("__Host-FluxerMcp-" + key, login.Correlation,
            CorrelationCookie(TimeSpan.FromMinutes(10)));

        return Results.Redirect(QueryHelpers.AddQueryString(options.ApiBaseUrl + "/v1/oauth2/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = options.ClientId, ["redirect_uri"] = login.Callback,
                ["response_type"] = "code", ["scope"] = FluxerOptions.Scope, ["state"] = key,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["prompt"] = "consent"
            }));
    }

    public IResult Callback(HttpContext context)
    {
        var query = context.Request.Query;
        var state = query["state"].ToString();
        if (query.Any(p => p.Value.Count != 1) || state.Length != 43)
            return Error("invalid_request");

        if (!cache.TryGetValue<Login>("login:" + state, out var login) || login is null)
            return Error("invalid_request");

        if (login.Callback != options.GetPublicUrl(context.Request) + CallbackPath)
            return Error("invalid_request");

        if (context.Request.Cookies["__Host-FluxerMcp-" + state] != login.Correlation)
            return Error("invalid_request");

        if (Interlocked.Exchange(ref login.Used, 1) != 0)
            return Error("invalid_request");
        cache.Remove("login:" + state);
        context.Response.Cookies.Delete("__Host-FluxerMcp-" + state, CorrelationCookie());
        var response = new Dictionary<string, string?>
        {
            ["state"] = login.State, ["iss"] = options.GetPublicUrl(context.Request)
        };
        var upstreamCode = query["code"].ToString();
        if (query.ContainsKey("error") || !Credential(upstreamCode, 256)) response["error"] = "access_denied";
        else
        {
            var code = Random();
            if (Store("code:" + code, new Code(login, upstreamCode), TimeSpan.FromMinutes(1)))
                response["code"] = code;
            else response["error"] = "temporarily_unavailable";
        }

        return Results.Redirect(QueryHelpers.AddQueryString(login.ClientRedirectUri, response));
    }

    public async Task<IResult> TokenAsync(HttpContext context)
    {
        var formResult = await ReadFormAsync(context);
        
        if (formResult is OAuthError error) 
            return Error(error.Error, error.StatusCode);
        if (formResult is not IFormCollection form)
            return Error("invalid_request");
        
        var fields = new Dictionary<string, string>();
        var grant = form["grant_type"].ToString();
        if (grant == "authorization_code")
        {
            var code = form["code"].ToString();
            var verifier = form["code_verifier"].ToString();
            if (code.Length != 43 || verifier.Length is < 43 or > 128)
                return Error("invalid_grant");

            if (!verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'))
                return Error("invalid_grant");

            if (!cache.TryGetValue<Code>("code:" + code, out var pending) || pending is null)
                return Error("invalid_grant");

            if (form["redirect_uri"] != pending.Login.ClientRedirectUri)
                return Error("invalid_grant");

            if (pending.Login.Callback != options.GetPublicUrl(context.Request) + CallbackPath)
                return Error("invalid_grant");

            if (WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != pending.Login.Challenge)
                return Error("invalid_grant");

            if (Interlocked.Exchange(ref pending.Used, 1) != 0)
                return Error("invalid_grant");
            cache.Remove("code:" + code);
            fields["code"] = pending.UpstreamCode;
            fields["redirect_uri"] = pending.Login.Callback;
            fields["code_verifier"] = verifier;
        }
        else if (grant == "refresh_token")
        {
            if (!Credential(form["refresh_token"].ToString())) return Error("invalid_request");
            fields["refresh_token"] = form["refresh_token"].ToString();
        }
        else return Error("unsupported_grant_type");

        if (form.ContainsKey("scope") && form["scope"] != FluxerOptions.Scope) return Error("invalid_scope");
        fields["grant_type"] = grant;
        return ToHttpResult(await ForwardAsync(context, fields, "token"));
    }

    public async Task<IResult> RevokeAsync(HttpContext context)
    {
        var formResult = await ReadFormAsync(context);
        if (formResult is OAuthError error) 
            return Error(error.Error, error.StatusCode);
        if (formResult is not IFormCollection form || !Credential(form["token"].ToString()))
            return Error("invalid_request");
        
        var hint = form["token_type_hint"].ToString();
        if (hint is not ("" or "access_token" or "refresh_token")) 
            return Error("invalid_request");
        
        var fields = new Dictionary<string, string>
        {
            ["token"] = form["token"].ToString()
        };
        if (hint.Length > 0)
            fields["token_type_hint"] = hint;
        
        return ToHttpResult(await ForwardAsync(context, fields, "token/revoke"));
    }

    private async Task<OAuthFormResult> ReadFormAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Headers.ContainsKey("Authorization") || request.ContentLength > 16384)
            return new OAuthError("invalid_request");

        if (!string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase))
            return new OAuthError("invalid_request");
        try
        {
            request.EnableBuffering(bufferThreshold: 16384, bufferLimit: 16384);
            var form = await request.ReadFormAsync(context.RequestAborted);
            if (form.Count > 16 || form.Any(p => p.Value.Count != 1 || p.Value.ToString().Length > 4096))
                return new OAuthError("invalid_request");

            if (form["client_id"] != options.ClientId || form.ContainsKey("client_secret"))
                return new OAuthError("invalid_request");

            if (form.ContainsKey("resource") && form["resource"] != options.GetPublicUrl(request) + "/mcp")
                return new OAuthError("invalid_request");
            return form;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or BadHttpRequestException)
        {
            return new OAuthError("invalid_request");
        }
    }

    private async Task<OAuthResult> ForwardAsync(HttpContext context, Dictionary<string, string> fields, string endpoint)
    {
        fields["client_id"] = options.ClientId;
        fields["client_secret"] = options.ClientSecret;
        try
        {
            using var client = clients.CreateClient(FluxerOptions.Scheme);
            using var form = new FormUrlEncodedContent(fields);
            // Single-use codes and rotating refresh tokens must never be retried automatically.
            using var response = await client.PostAsync(options.ApiBaseUrl + "/v1/oauth2/" + endpoint,
                form, context.RequestAborted);
            if (endpoint == "token/revoke" && response.IsSuccessStatusCode)
                return new Revoked();
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                return new OAuthError("temporarily_unavailable", 503);
            var body = await response.Content.ReadAsStringAsync(context.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                var error = JsonSerializer.Deserialize<ErrorResponse>(body)?.Error;
                if (error is "invalid_grant" or "invalid_request" or "invalid_scope" or "unsupported_grant_type")
                    return new OAuthError(error);
                
                return new OAuthError("server_error", 502);
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body);
            if (token is null)
                return new OAuthError("server_error", 502);

            if (!Credential(token.AccessToken) || !Credential(token.RefreshToken))
                return new OAuthError("server_error", 502);

            if (token.ExpiresIn <= 0 || token.TokenType != "Bearer")
                return new OAuthError("server_error", 502);

            if (token.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(FluxerOptions.Scope) != true)
                return new OAuthError("server_error", 502);
            
            return token;
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
        {
            return new OAuthError("server_error", 502);
        }
    }

    private static IResult ToHttpResult(OAuthResult result)
    {
        switch (result)
        {
            case TokenResponse token:
                return Results.Json(token);
            case Revoked:
                return Results.Ok();
            case OAuthError error:
                return Error(error.Error, error.StatusCode);
            default:
                return Error("server_error", 502);
        }
    }

    private bool Store(string key, object value, TimeSpan lifetime)
    {
        cache.Set(key, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime, Size = 1 });
        return cache.TryGetValue(key, out _);
    }

    private static CookieOptions CorrelationCookie(TimeSpan? maxAge = null)
    {
        return new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = maxAge
        };
    }

    private static bool Credential(string? value, int maximum = 4096)
    {
        return value is { Length: > 0 } && value.Length <= maximum && value.All(c => c is >= '!' and <= '~');
    }

    private static string Random()
    {
        return WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    }

    private static IResult Error(string error, int status = 400)
    {
        return Results.Json(new ErrorResponse(error), statusCode: status);
    }
}
