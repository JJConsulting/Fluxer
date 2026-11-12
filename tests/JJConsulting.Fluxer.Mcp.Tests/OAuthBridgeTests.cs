using System.Net;
using JJConsulting.Fluxer.Mcp.Authentication;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class OAuthBridgeTests
{
    private static readonly string Verifier = new('a', 43);
    private static readonly string Challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)));

    private static Dictionary<string, string?> Parameters() => new()
    {
        ["client_id"] = "123", ["redirect_uri"] = McpFactory.Callback, ["response_type"] = "code",
        ["scope"] = "identify", ["state"] = "chatgpt-state", ["resource"] = McpFactory.Origin + "/mcp",
        ["code_challenge"] = Challenge, ["code_challenge_method"] = "S256"
    };

    private static async Task<string> BeginAsync(HttpClient client)
    {
        using var response = await client.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", Parameters()));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var destination = response.Headers.Location!;
        Assert.StartsWith("https://fluxer.test/api/v1/oauth2/authorize?", destination.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(destination.Query);
        Assert.Equal(McpFactory.Origin + "/auth/fluxer/callback", query["redirect_uri"]);
        Assert.Equal(Challenge, query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("identify", query["scope"]);
        Assert.NotEqual("chatgpt-state", query["state"].ToString());
        Assert.DoesNotContain("fixture-secret", destination.AbsoluteUri);
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie =>
        {
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        });
        return query["state"].ToString();
    }

    private static async Task<string> AuthorizeAsync(HttpClient client)
    {
        var state = await BeginAsync(client);
        using var response = await client.GetAsync("/auth/fluxer/callback?code=fixture-code&state=" + state);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(McpFactory.Callback + "?", response.Headers.Location!.AbsoluteUri);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("chatgpt-state", query["state"]);
        Assert.Equal(McpFactory.Origin, query["iss"]);
        Assert.NotEqual("fixture-code", query["code"].ToString());
        return query["code"].ToString();
    }

    private static Dictionary<string, string> TokenForm(string code, string? verifier = null) => new()
    {
        ["client_id"] = "123", ["grant_type"] = "authorization_code", ["code"] = code,
        ["redirect_uri"] = McpFactory.Callback, ["resource"] = McpFactory.Origin + "/mcp",
        ["code_verifier"] = verifier ?? Verifier
    };

    [Fact]
    public async Task Login_Exchanges_The_Upstream_Code_Without_A_Client_Secret_From_ChatGPT()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var code = await AuthorizeAsync(browser);
        using var response = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("fixture-access", json.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("fixture-refresh", json.RootElement.GetProperty("refresh_token").GetString());
        Assert.DoesNotContain("fixture-secret", body);
        Assert.DoesNotContain("fixture-admin", body);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
        using var profile = await McpFactory.ToolAsync(browser, "fixture-access");
        Assert.Contains("456", await profile.Content.ReadAsStringAsync());
        using var replay = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
        Assert.DoesNotContain("fixture-secret", string.Join('\n', factory.Logs));
        Assert.DoesNotContain("fixture-code", string.Join('\n', factory.Logs));
        Assert.DoesNotContain("fixture-access", string.Join('\n', factory.Logs));
    }

    [Fact]
    public async Task Multiple_Callbacks_Keep_Concurrent_Logins_And_Codes_Bound_To_Their_Destination()
    {
        const string otherCallback = "https://other-client.test/callback";
        using var factory = new McpFactory();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<FluxerOptions>(options =>
                options.AllowedClientRedirectUris = [McpFactory.Callback, otherCallback])));
        using var browser = configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(McpFactory.Origin), AllowAutoRedirect = false, HandleCookies = true
        });
        var firstState = await BeginAsync(browser);
        var parameters = Parameters();
        parameters["redirect_uri"] = otherCallback;
        using var secondLogin = await browser.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters));
        Assert.Equal(HttpStatusCode.Redirect, secondLogin.StatusCode);
        var secondState = QueryHelpers.ParseQuery(secondLogin.Headers.Location!.Query)["state"].ToString();

        foreach (var (state, destination, wrongDestination) in new[]
                 {
                     (secondState, otherCallback, McpFactory.Callback),
                     (firstState, McpFactory.Callback, otherCallback)
                 })
        {
            using var callback = await browser.GetAsync("/auth/fluxer/callback?code=fixture-code&state=" + state);
            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal(destination, callback.Headers.Location!.GetLeftPart(UriPartial.Path));
            var code = QueryHelpers.ParseQuery(callback.Headers.Location.Query)["code"].ToString();
            var form = TokenForm(code);
            form["redirect_uri"] = wrongDestination;
            using var invalid = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            form["redirect_uri"] = destination;
            using var valid = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        }

        parameters["redirect_uri"] = "https://attacker.test/callback";
        using var rejected = await browser.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Null(rejected.Headers.Location);
        Assert.Equal(2, factory.Fluxer.TokenExchanges);
    }

    [Theory]
    [InlineData("redirect_uri", "https://attacker.test/callback")]
    [InlineData("client_id", "999")]
    [InlineData("code_challenge_method", "plain")]
    [InlineData("code_challenge", "invalid")]
    [InlineData("resource", "https://attacker.test/mcp")]
    [InlineData("scope", "email")]
    [InlineData("response_type", "token")]
    [InlineData("state", "")]
    public async Task Invalid_Authorization_Never_Redirects(string field, string value)
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser();
        var parameters = Parameters();
        parameters[field] = value;
        using var response = await browser.GetAsync(QueryHelpers.AddQueryString("/oauth/authorize", parameters));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(0, factory.Fluxer.TokenExchanges);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("cookie")]
    [InlineData("replay")]
    public async Task Callback_Requires_The_Original_State_And_Browser_And_Is_Used_Once(string scenario)
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var state = await BeginAsync(browser);
        var callback = "/auth/fluxer/callback?code=fixture-code&state=" + state;
        if (scenario == "replay")
        {
            using var first = await browser.GetAsync(callback);
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        }
        using var stranger = factory.Browser();
        using var response = scenario == "cookie" ? await stranger.GetAsync(callback) :
            await browser.GetAsync(scenario == "state" ? callback + "invalid" : callback);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.TokenExchanges);
    }

    [Theory]
    [InlineData("code_verifier", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    [InlineData("redirect_uri", "https://attacker.test/callback")]
    [InlineData("client_id", "999")]
    [InlineData("resource", "https://attacker.test/mcp")]
    [InlineData("code", "fixture-code")]
    [InlineData("grant_type", "client_credentials")]
    [InlineData("client_secret", "injected")]
    public async Task Invalid_Redemption_Never_Contacts_The_Upstream(string field, string value)
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var code = await AuthorizeAsync(browser);
        var form = TokenForm(code);
        form[field] = value;
        using var response = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.TokenExchanges);
    }

    [Fact]
    public async Task Invalid_Verifier_Does_Not_Consume_The_Code()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var code = await AuthorizeAsync(browser);
        using var invalid = await browser.PostAsync("/oauth/token",
            new FormUrlEncodedContent(TokenForm(code, new string('b', 43))));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, factory.Fluxer.TokenExchanges);

        using var valid = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
    }

    [Fact]
    public async Task Callback_Without_Correlation_Does_Not_Consume_The_Login()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var state = await BeginAsync(browser);
        var callback = "/auth/fluxer/callback?code=fixture-code&state=" + state;
        using var stranger = factory.Browser();
        using var invalid = await stranger.GetAsync(callback);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var valid = await browser.GetAsync(callback);
        Assert.Equal(HttpStatusCode.Redirect, valid.StatusCode);
        Assert.True(QueryHelpers.ParseQuery(valid.Headers.Location!.Query).ContainsKey("code"));
    }

    [Fact]
    public async Task Concurrent_Code_Redemption_Only_Forwards_Once()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var code = await AuthorizeAsync(browser);
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)))));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
        foreach (var response in responses) response.Dispose();
    }

    [Theory]
    [InlineData("refresh_token")]
    [InlineData(null)]
    public async Task Refresh_And_Revocation_Inject_Only_The_Server_Credentials(string? hint)
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser();
        using var refresh = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "123", ["grant_type"] = "refresh_token", ["refresh_token"] = "fixture-refresh"
        }));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var revokeForm = new Dictionary<string, string> { ["client_id"] = "123", ["token"] = "fixture-refresh" };
        if (hint is not null) revokeForm["token_type_hint"] = hint;
        using var revoke = await browser.PostAsync("/oauth/revoke", new FormUrlEncodedContent(revokeForm));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
        Assert.Equal(1, factory.Fluxer.Revocations);
    }

    [Fact]
    public async Task User_Denial_Returns_The_Original_State_To_The_Approved_Callback()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser(cookies: true);
        var state = await BeginAsync(browser);
        using var response = await browser.GetAsync("/auth/fluxer/callback?error=access_denied&state=" + state);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("chatgpt-state", query["state"]);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal(McpFactory.Origin, query["iss"]);
        Assert.Equal(McpFactory.Callback, response.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.False(query.ContainsKey("code"));
    }

    [Theory]
    [InlineData("transport", HttpStatusCode.BadGateway)]
    [InlineData("invalid_json", HttpStatusCode.BadGateway)]
    [InlineData("upstream_error", HttpStatusCode.ServiceUnavailable)]
    [InlineData("invalid_grant", HttpStatusCode.BadRequest)]
    public async Task Upstream_Failures_Are_Sanitized_And_Never_Retried(string failure, HttpStatusCode expected)
    {
        using var factory = new McpFactory();
        if (failure == "transport") factory.Fluxer.FailTokenTransport = true;
        if (failure == "invalid_json") factory.Fluxer.TokenBody = "upstream-secret";
        if (failure == "upstream_error") factory.Fluxer.TokenStatus = HttpStatusCode.ServiceUnavailable;
        if (failure == "invalid_grant")
        {
            factory.Fluxer.TokenStatus = HttpStatusCode.BadRequest;
            factory.Fluxer.TokenBody = "{\"error\":\"invalid_grant\",\"error_description\":\"upstream-secret\"}";
        }
        using var browser = factory.Browser(cookies: true);
        var code = await AuthorizeAsync(browser);
        using var response = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)));
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain("upstream-secret", await response.Content.ReadAsStringAsync());
        using var replay = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(TokenForm(code)));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(1, factory.Fluxer.TokenExchanges);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("missing_refresh")]
    [InlineData("null_scope")]
    [InlineData("numeric_string")]
    [InlineData("wrong_type")]
    public async Task Invalid_Token_Payloads_Are_Rejected(string scenario)
    {
        using var factory = new McpFactory();
        var payload = new Dictionary<string, object?>
        {
            ["access_token"] = "fixture-access", ["refresh_token"] = "fixture-refresh",
            ["token_type"] = "Bearer", ["expires_in"] = 3600, ["scope"] = "identify"
        };
        switch (scenario)
        {
            case "missing_refresh": payload.Remove("refresh_token"); break;
            case "null_scope": payload["scope"] = null; break;
            case "numeric_string": payload["expires_in"] = "3600"; break;
            case "wrong_type": payload["access_token"] = 123; break;
        }
        factory.Fluxer.TokenBody = scenario is "null" or "{}" or "[]"
            ? scenario : JsonSerializer.Serialize(payload);
        using var browser = factory.Browser();
        using var response = await browser.PostAsync("/oauth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = "123", ["grant_type"] = "refresh_token", ["refresh_token"] = "fixture-refresh"
            }));
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("server_error", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Form_Size_And_Duplicate_Parameters_Are_Rejected()
    {
        using var factory = new McpFactory();
        using var browser = factory.Browser();
        using var large = await browser.PostAsync("/oauth/token", new StringContent(new string('x', 17000), Encoding.UTF8,
            "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.BadRequest, large.StatusCode);
        using var duplicate = await browser.PostAsync("/oauth/token", new StringContent(
            "client_id=123&client_id=999&grant_type=refresh_token&refresh_token=fixture-refresh", Encoding.UTF8,
            "application/x-www-form-urlencoded"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(0, factory.Fluxer.TokenExchanges);
    }

    [Fact]
    public async Task Restarted_Bridge_Does_Not_Accept_Pending_Logins()
    {
        using var first = new McpFactory();
        using var browser = first.Browser(cookies: true);
        var state = await BeginAsync(browser);
        using var restarted = new McpFactory();
        using var newBrowser = restarted.Browser();
        using var response = await newBrowser.GetAsync("/auth/fluxer/callback?code=fixture-code&state=" + state);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, restarted.Fluxer.TokenExchanges);
    }
}
