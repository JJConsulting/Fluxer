using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Mcp.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class McpFactory(string? publicUrl = null) : WebApplicationFactory<Program>
{
    public const string Callback = "https://chatgpt.com/connector_platform_oauth_redirect";
    public const string Origin = "https://mcp.test";
    public readonly FakeFluxer Fluxer = new();
    public readonly ConcurrentQueue<string> Logs = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["AllowedHosts"] = "mcp.test", ["Fluxer:PublicUrl"] = publicUrl ?? "",
            ["Fluxer:ApiBaseUrl"] = "https://fluxer.test/api", ["Fluxer:ClientId"] = "123",
            ["Fluxer:ClientSecret"] = "fixture-secret", ["Fluxer:ClientRedirectUri"] = Callback, ["Fluxer:AdminApiKey"] = "fixture-admin"
        }) builder.UseSetting(key, value);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILoggerProvider>(new CaptureLogs(Logs));
            services.AddHttpClient(FluxerOptions.Scheme).ConfigurePrimaryHttpMessageHandler(() => Fluxer);
            services.AddHttpClient<FluxerAdminClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Fluxer);
        });
    }

    public HttpClient Browser(bool cookies = false) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(Origin), AllowAutoRedirect = false, HandleCookies = cookies
    });

    public static Task<HttpResponseMessage> ToolAsync(HttpClient client, string? token, string name = "get_profile",
        object? arguments = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        if (token is not null) request.Headers.Authorization = new("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name, arguments = arguments ?? new { } }
        }), Encoding.UTF8, "application/json");
        return client.SendAsync(request);
    }
}

public sealed class FakeFluxer : HttpMessageHandler
{
    public readonly Dictionary<string, object?> Claims = new()
    {
        ["active"] = true, ["client_id"] = "123", ["token_type"] = "Bearer", ["scope"] = "identify",
        ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
    };
    public HttpStatusCode IntrospectionStatus = HttpStatusCode.OK;
    public string? IntrospectionBody;
    public bool ThrowOnIntrospection;
    public int? ChannelType = 1;
    public bool? Nsfw = false;
    public string[]? Recipients;
    public bool FailMessages;
    public string GuildOwner = "456";
    public bool GuildNsfw;
    public bool GuildChannelNsfw;
    public int GuildChannelType;
    public bool FailGuilds;
    public bool LaterPage;
    public bool TwoChannels;
    public bool MoreMessages;
    public bool IncludeNames = true;
    public bool SearchWrongChannel;
    public int SearchTotal = 1;
    public int SearchReads;
    public string? SearchQuery;
    public string? LastMessageBefore;
    public DateTimeOffset MessageTime = DateTimeOffset.Parse("2026-11-11T10:00:00Z");
    public readonly List<string> ReadChannels = [];
    public int TokenExchanges;
    public int Revocations;
    public string? TokenBody;
    public HttpStatusCode TokenStatus = HttpStatusCode.OK;
    public bool FailTokenTransport;
    public int MessageReads;
    public int Introspections;
    public readonly List<string> DmUsers = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.AbsolutePath;
        Assert.Equal("fluxer.test", request.RequestUri.Host);
        if (path.EndsWith("/token") || path.EndsWith("/token/revoke"))
        {
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("123", form["client_id"]);
            Assert.Equal("fixture-secret", form["client_secret"]);
            if (path.EndsWith("/revoke"))
            {
                Revocations++;
                Assert.False(form.ContainsKey("token_type_hint") && form["token_type_hint"] == "");
                Assert.Equal("fixture-refresh", form["token"]);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
            TokenExchanges++;
            if (FailTokenTransport) throw new HttpRequestException("upstream-secret");
            if (form["grant_type"] == "authorization_code")
            {
                Assert.Equal("fixture-code", form["code"]);
                Assert.Equal(McpFactory.Origin + "/auth/fluxer/callback", form["redirect_uri"]);
                Assert.Equal(new string('a', 43), form["code_verifier"]);
            }
            else
            {
                Assert.Equal("refresh_token", form["grant_type"]);
                Assert.Equal("fixture-refresh", form["refresh_token"]);
            }
            return new HttpResponseMessage(TokenStatus)
            {
                Content = new StringContent(TokenBody ?? JsonSerializer.Serialize(new
                {
                    access_token = "fixture-access", refresh_token = "fixture-refresh",
                    token_type = "Bearer", expires_in = 3600, scope = "identify"
                }))
            };
        }
        if (path.EndsWith("/introspect"))
        {
            Introspections++;
            Assert.Null(request.Headers.Authorization);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("123", form["client_id"]);
            Assert.Equal("fixture-secret", form["client_secret"]);
            if (ThrowOnIntrospection) throw new HttpRequestException("upstream-secret");
            var claims = new Dictionary<string, object?>(Claims);
            if (!claims.ContainsKey("sub")) claims["sub"] = form["token"] == "second-user-token" ? "567" : "456";
            return new HttpResponseMessage(IntrospectionStatus)
            {
                Content = new StringContent(IntrospectionBody ?? JsonSerializer.Serialize(claims)),
                Headers = { Location = new Uri("https://unapproved.test/") }
            };
        }
        Assert.StartsWith("/api/v1/admin/", path);
        Assert.True(request.Headers.Authorization is { Scheme: "Admin", Parameter: "fixture-admin" });
        if (path.EndsWith("/guilds"))
        {
            Assert.Equal("456", path.Split('/')[5]);
            if (FailGuilds) return new(HttpStatusCode.Forbidden);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            return Json(new { guilds = query.ContainsKey("after") ? Array.Empty<object>() :
                new object[] { new { id = "3000", name = "Fixture guild" } } });
        }
        if (path == "/api/v1/admin/guilds/3000")
            return Json(new { guild = new { id = "3000", owner_id = GuildOwner, nsfw = GuildNsfw,
                channels = new[] { new { id = "4000", name = "general", type = GuildChannelType, nsfw = GuildChannelNsfw } } } });
        if (path.EndsWith("/dm-channels"))
        {
            var user = path.Split('/')[5];
            DmUsers.Add(user);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            var firstPage = !query.ContainsKey("before");
            var laterPage = LaterPage && query.TryGetValue("before", out var before) && before == "1500";
            string[] ids = firstPage || laterPage
                ? [LaterPage && firstPage ? "1500" : user == "456" ? "1000" : "1001"] : [];
            if (TwoChannels && firstPage) ids = [.. ids, "999"];
            var recipientIds = Recipients ?? [user, "789"];
            var channels = ids.Take(int.Parse(query["limit"]!)).Select(id => new
            {
                channel_id = id, channel_type = ChannelType, channel_nsfw = Nsfw, recipient_ids = recipientIds,
                recipients = IncludeNames ? recipientIds.Select(recipient => new
                {
                    id = recipient, username = recipient == "789" ? "gabriel" : "user" + recipient,
                    global_name = recipient == "789" ? "Gabriel Test" : "Authenticated User"
                }).ToArray() : [],
                last_message_id = id == "999" ? "5000" : "2000"
            }).ToArray();
            return Json(new { channels });
        }
        Assert.Contains("/messages", path);
        var parameters = QueryHelpers.ParseQuery(request.RequestUri.Query);
        var searching = path == "/api/v1/admin/messages";
        var channel = searching ? parameters["channel_id"].ToString() : path.Split('/')[5];
        ReadChannels.Add(channel);
        if (searching)
        {
            SearchReads++;
            SearchQuery = parameters["q"].ToString();
        }
        else
        {
            MessageReads++;
            LastMessageBefore = parameters.TryGetValue("before", out var before) ? before.ToString() : null;
        }
        if (FailMessages) return new(HttpStatusCode.Forbidden) { Content = new StringContent("upstream-secret") };
        var messages = new[] { new
        {
            id = channel == "999" ? "5000" : "2000", channel_id = SearchWrongChannel ? "8888" : channel,
            author_id = "789", author_username = IncludeNames ? "gabriel" : null,
            author_global_name = IncludeNames ? "Gabriel Test" : null,
            content = "fixture message", timestamp = channel == "999" ? MessageTime.AddHours(2) : MessageTime
        } };
        return searching ? Json(new { messages, total = SearchTotal }) : Json(new { messages, has_more = MoreMessages });
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
    };
}

internal sealed class CaptureLogs(ConcurrentQueue<string> entries) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(entries);
    public void Dispose() { }
    private sealed class CaptureLogger(ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => entries.Enqueue(formatter(state, exception));
    }
}
