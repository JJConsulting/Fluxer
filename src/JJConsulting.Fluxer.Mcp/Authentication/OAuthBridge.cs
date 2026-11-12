using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace JJConsulting.Fluxer.Mcp.Authentication;

// shortcut: pending logins live in one process; use shared state before running multiple replicas.
public sealed class OAuthBridge(FluxerOptions options, IHttpClientFactory clients) : IDisposable
{
    public const string CallbackPath = "/auth/fluxer/callback";
    private readonly MemoryCache _pending = new(new MemoryCacheOptions { SizeLimit = 1000 });

    private sealed record Login(string State, string Challenge, string Callback, string Correlation)
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
            query["redirect_uri"] != options.ClientRedirectUri)
            return Error("invalid_request");
        var state = query["state"].ToString();
        var challenge = query["code_challenge"].ToString();
        if (state.Length is < 1 or > 256 || query["response_type"] != "code" ||
            query["code_challenge_method"] != "S256" || challenge.Length != 43 ||
            !challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ||
            query["resource"] != options.GetPublicUrl(context.Request) + "/mcp")
            return Error("invalid_request");
        if (query["scope"] != FluxerOptions.Scope) return Error("invalid_scope");

        var key = Random();
        var login = new Login(state, challenge, options.GetPublicUrl(context.Request) + CallbackPath, Random());
        _pending.Set("login:" + key, login, new MemoryCacheEntryOptions
            { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        if (!_pending.TryGetValue("login:" + key, out _)) return Error("temporarily_unavailable", 503);
        context.Response.Cookies.Append("__Host-FluxerMcp-" + key, login.Correlation, new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
            MaxAge = TimeSpan.FromMinutes(10)
        });
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
        if (query.Any(p => p.Value.Count != 1) || state.Length != 43 ||
            !_pending.TryGetValue<Login>("login:" + state, out var login) || login is null ||
            login.Callback != options.GetPublicUrl(context.Request) + CallbackPath ||
            context.Request.Cookies["__Host-FluxerMcp-" + state] != login.Correlation ||
            Interlocked.Exchange(ref login.Used, 1) != 0)
            return Error("invalid_request");
        _pending.Remove("login:" + state);
        context.Response.Cookies.Delete("__Host-FluxerMcp-" + state,
            new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/" });
        var response = new Dictionary<string, string?>
        {
            ["state"] = login.State, ["iss"] = options.GetPublicUrl(context.Request)
        };
        var upstreamCode = query["code"].ToString();
        if (query.ContainsKey("error") || !Credential(upstreamCode, 256)) response["error"] = "access_denied";
        else
        {
            var code = Random();
            _pending.Set("code:" + code, new Code(login, upstreamCode), new MemoryCacheEntryOptions
                { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1), Size = 1 });
            if (!_pending.TryGetValue("code:" + code, out _)) response["error"] = "temporarily_unavailable";
            else response["code"] = code;
        }

        return Results.Redirect(QueryHelpers.AddQueryString(options.ClientRedirectUri, response));
    }

    public async Task<IResult> TokenAsync(HttpContext context)
    {
        var form = await ReadFormAsync(context);
        if (form is null) return Error("invalid_request");
        var fields = new Dictionary<string, string>();
        var grant = form["grant_type"].ToString();
        if (grant == "authorization_code")
        {
            var code = form["code"].ToString();
            var verifier = form["code_verifier"].ToString();
            if (code.Length != 43 || verifier.Length is < 43 or > 128 ||
                !verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~') ||
                !_pending.TryGetValue<Code>("code:" + code, out var pending) || pending is null ||
                form["redirect_uri"] != options.ClientRedirectUri ||
                pending.Login.Callback != options.GetPublicUrl(context.Request) + CallbackPath ||
                WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) !=
                pending.Login.Challenge ||
                Interlocked.Exchange(ref pending.Used, 1) != 0)
                return Error("invalid_grant");
            _pending.Remove("code:" + code);
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
        return await ForwardAsync(context, fields, "token");
    }

    public async Task<IResult> RevokeAsync(HttpContext context)
    {
        var form = await ReadFormAsync(context);
        if (form is null || !Credential(form["token"].ToString())) return Error("invalid_request");
        var hint = form["token_type_hint"].ToString();
        if (hint is not ("" or "access_token" or "refresh_token")) return Error("invalid_request");
        var fields = new Dictionary<string, string> { ["token"] = form["token"].ToString() };
        if (hint.Length > 0) fields["token_type_hint"] = hint;
        return await ForwardAsync(context, fields, "token/revoke");
    }

    private async Task<IFormCollection?> ReadFormAsync(HttpContext context)
    {
        var request = context.Request;
        if (request.Headers.ContainsKey("Authorization") || request.ContentLength > 16384 ||
            !string.Equals(request.ContentType?.Split(';')[0].Trim(), "application/x-www-form-urlencoded",
                StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            request.EnableBuffering(bufferThreshold: 16384, bufferLimit: 16384);
            var form = await request.ReadFormAsync(context.RequestAborted);
            if (form.Count > 16 || form.Any(p => p.Value.Count != 1 || p.Value.ToString().Length > 4096) ||
                form["client_id"] != options.ClientId || form.ContainsKey("client_secret") ||
                form.ContainsKey("resource") && form["resource"] != options.GetPublicUrl(request) + "/mcp") return null;
            return form;
        }
        catch (Exception error) when (error is InvalidDataException or IOException or BadHttpRequestException)
        {
            return null;
        }
    }

    private async Task<IResult> ForwardAsync(HttpContext context, Dictionary<string, string> fields, string endpoint)
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
            if (endpoint == "token/revoke" && response.IsSuccessStatusCode) return Results.Ok();
            if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
                return Error("temporarily_unavailable", 503);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(context.RequestAborted));
            var json = document.RootElement;
            if (!response.IsSuccessStatusCode)
            {
                var error = json.TryGetProperty("error", out var value) ? value.GetString() : null;
                return error is "invalid_grant" or "invalid_request" or "invalid_scope" or "unsupported_grant_type"
                    ? Error(error)
                    : Error("server_error", 502);
            }

            var access = json.GetProperty("access_token").GetString();
            var refresh = json.GetProperty("refresh_token").GetString();
            var scope = json.GetProperty("scope").GetString();
            var expires = json.GetProperty("expires_in").GetInt64();
            if (!Credential(access) || !Credential(refresh) || expires <= 0 ||
                json.GetProperty("token_type").GetString() != "Bearer" ||
                scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(FluxerOptions.Scope) != true)
                return Error("server_error", 502);
            return Results.Json(new
            {
                access_token = access, refresh_token = refresh, token_type = "Bearer",
                expires_in = expires, scope
            });
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
                                          or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return Error("server_error", 502);
        }
    }

    private static bool Credential(string? value, int maximum = 4096) =>
        value is { Length: > 0 } && value.Length <= maximum && value.All(c => c is >= '!' and <= '~');

    private static string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static IResult Error(string error, int status = 400) => Results.Json(new { error }, statusCode: status);
    public void Dispose() => _pending.Dispose();
}