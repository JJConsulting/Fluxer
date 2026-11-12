using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client.Tests;

public class FluxerClientTests
{
    private const string UserId = "1570137904737419264";
    private const string ChannelId = "1502771119476146176";
    private const string AdminKey = "sentinel-admin-secret";
    private const string OAuthToken = "sentinel-oauth-secret";

    // Synthetic projections of the official Admin/OAuth schemas, never production captures.
    private static string Dms(params string[] ids)
    {
        return JsonSerializer.Serialize(new
        {
            channels = ids.Select(id => new
            {
                channel_id = id, channel_type = 1, recipient_ids = new[] { UserId },
                recipients = Array.Empty<object>(), last_message_id = (string?)null, is_open = true
            })
        });
    }

    private static string Messages(bool more, params string[] ids)
    {
        return JsonSerializer.Serialize(new
        {
            messages = ids.Select(id => new
            {
                id, channel_id = ChannelId, author_id = UserId, content = "sentinel-message-content",
                timestamp = "2026-11-11T12:00:00.000Z", attachments = Array.Empty<object>()
            }),
            has_more = more,
            message_responses = Array.Empty<object>()
        });
    }

    private static FluxerClientOptions Settings(int maxRetries = 2, int maxPages = 10,
        int maxBytes = 1_048_576, TimeSpan? timeout = null, TimeSpan? maxDelay = null)
    {
        return new FluxerClientOptions
        {
            ApiBaseUrl = "https://fluxer.example/api/prefix",
            AdminApiKey = AdminKey,
            DmPageSize = 2,
            MaxDmPages = maxPages,
            MaxRetries = maxRetries,
            MaxResponseBytes = maxBytes,
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(5),
            MaxRetryDelay = maxDelay ?? TimeSpan.FromMilliseconds(500)
        };
    }

    private static FluxerAdminClient Admin(HttpClient http, FluxerClientOptions? options = null)
    {
        return new FluxerAdminClient(http, Options.Create(options ?? Settings()));
    }

    private static FluxerIdentityClient Identity(HttpClient http, FluxerClientOptions? options = null)
    {
        return new FluxerIdentityClient(http, Options.Create(options ?? Settings()));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    [Fact]
    public async Task Guild_Queries_Use_Admin_Transport_And_Return_Typed_Data()
    {
        using var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(Json("""{"guilds":[{"id":"3000","name":"Example"}]}""")),
            (_, _) => Task.FromResult(Json("""{"guild":{"id":"3000","owner_id":"456","nsfw":false,"channels":[{"id":"4000","name":"general","type":0,"nsfw":false}]}}""")));
        using var http = new HttpClient(handler);
        var client = Admin(http);
        var page = Success(await client.GetUserGuildPageAsync(UserId, new FluxerPageRequest(2, after: "2000")));
        Assert.Equal("Example", Assert.Single(page.Guilds).Name);
        var guild = Success(await client.GetGuildAsync("3000"));
        Assert.Equal("456", guild.OwnerId);
        Assert.False(guild.Nsfw);
        Assert.Equal("4000", Assert.Single(guild.Channels).Id);
        Assert.EndsWith($"/v1/admin/users/{UserId}/guilds?limit=2&after=2000", handler.Requests[0].Url);
        Assert.EndsWith("/v1/admin/guilds/3000", handler.Requests[1].Url);
        Assert.All(handler.Requests, r => Assert.Equal($"Admin {AdminKey}", r.Authorization));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"guilds\":null}")]
    [InlineData("{\"guilds\":[{\"id\":\"../3000\",\"name\":\"Example\"}]}")]
    [InlineData("{\"guilds\":[{\"id\":\"2000\",\"name\":\"Example\"}]}")]
    [InlineData("{\"guilds\":[{\"id\":\"3000\",\"name\":\"Example\"},{\"id\":\"3000\",\"name\":\"Duplicate\"}]}")]
    public async Task Guild_Pages_Reject_Unverifiable_Responses(string body)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var result = await Admin(http).GetUserGuildPageAsync(UserId, new FluxerPageRequest(2, after: "2000"));
        Assert.True(result is FluxerError { Failure: FluxerFailure.InvalidResponse });
    }

    [Theory]
    [InlineData("{\"guild\":null}")]
    [InlineData("{\"guild\":{\"id\":\"9999\",\"owner_id\":\"456\",\"channels\":[]}}")]
    [InlineData("{\"guild\":{\"id\":\"3000\",\"owner_id\":\"invalid\",\"channels\":[]}}")]
    [InlineData("{\"guild\":{\"id\":\"3000\",\"owner_id\":\"456\",\"channels\":null}}")]
    public async Task Guild_Lookup_Rejects_Unverifiable_Responses(string body)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var result = await Admin(http).GetGuildAsync("3000");
        Assert.True(result is FluxerError { Failure: FluxerFailure.InvalidResponse });
    }

    [Fact]
    public async Task Complete_Dm_Listing_Follows_Numeric_Cursor_Until_Empty_Even_After_A_Short_Page()
    {
        // Numeric min is 9, not the lexicographic min 100.
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Dms("100", "9"))),
            (_, _) => Task.FromResult(Json(Dms("8"))),
            (_, _) => Task.FromResult(Json(Dms())));
        using var http = new HttpClient(handler);
        var result = Success(await Admin(http).GetAllIndividualDmsAsync(UserId));

        Assert.Equal(new[] { "100", "9", "8" }, result.Select(c => c.ChannelId));
        Assert.EndsWith($"/api/prefix/v1/admin/users/{UserId}/dm-channels?type=dm&limit=2", handler.Requests[0].Url);
        Assert.EndsWith("&before=9", handler.Requests[1].Url);
        Assert.EndsWith("&before=8", handler.Requests[2].Url);
        Assert.All(handler.Requests, r => Assert.Equal($"Admin {AdminKey}", r.Authorization));
    }

    [Fact]
    public async Task Message_Pages_Preserve_Order_And_Large_String_Ids_And_Allow_Empty_Continuation()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Messages(true, "9007199254740993", "9007199254740994"))),
            (_, _) => Task.FromResult(Json(Messages(false))));
        using var http = new HttpClient(handler);
        var client = Admin(http);
        var first = Success(await client.GetMessagesAsync(ChannelId, new FluxerPageRequest(2)));
        Assert.True(first.HasMore);
        Assert.Equal("9007199254740993", first.Messages[0].Id);
        Assert.Equal("sentinel-message-content", first.Messages[0].Content);
        Assert.Equal(TimeSpan.Zero, first.Messages[0].Timestamp.Offset);
        var last = Success(await client.GetMessagesAsync(ChannelId, new FluxerPageRequest(2, before: first.Messages[0].Id)));
        Assert.Empty(last.Messages);
        Assert.False(last.HasMore);
        Assert.EndsWith("&before=9007199254740993", handler.Requests[1].Url);
    }

    [Fact]
    public async Task After_Cursor_And_Unknown_Channel_Empty_Page_Follow_Documented_Contract()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Messages(false, "12", "11"))),
            (_, _) => Task.FromResult(Json(Messages(false))));
        using var http = new HttpClient(handler);
        var first = Success(await Admin(http).GetMessagesAsync(ChannelId, new FluxerPageRequest(50, after: "10")));
        Assert.Equal(new[] { "12", "11" }, first.Messages.Select(m => m.Id));
        Assert.EndsWith("limit=50&after=10", handler.Requests[0].Url);
        var missing = Success(await Admin(http).GetMessagesAsync("123"));
        Assert.Empty(missing.Messages);
        Assert.False(missing.HasMore);
    }

    [Fact]
    public async Task An_Empty_Dm_Page_Is_A_Valid_Complete_Result()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Dms())));
        using var http = new HttpClient(handler);
        Assert.Empty(Success(await Admin(http).GetAllIndividualDmsAsync(UserId)));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"channels\":null}")]
    [InlineData("{\"channels\":[null]}")]
    [InlineData("{\"channels\":[{\"channel_id\":123,\"channel_type\":1,\"recipient_ids\":[]}]}")]
    [InlineData("{\"channels\":[{\"channel_id\":\"123\",\"channel_type\":3,\"recipient_ids\":[]}]}")]
    [InlineData("{\"channels\":[{\"channel_id\":\"123\",\"channel_type\":null,\"recipient_ids\":[]}]}")]
    [InlineData("{\"channels\":[{\"channel_id\":\"123\",\"channel_type\":999,\"recipient_ids\":[]}]}")]
    [InlineData("{\"channels\":[{\"channel_id\":\"123\",\"channel_type\":1,\"recipient_ids\":[null]}]}")]
    public async Task Invalid_Or_Non_Individual_Dm_Response_Fails_Closed(string body)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetAllIndividualDmsAsync(UserId)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"messages\":[],\"has_more\":true}")]
    [InlineData("{\"messages\":[null],\"has_more\":false}")]
    [InlineData("{\"messages\":null,\"has_more\":false}")]
    public async Task Invalid_Message_Page_Is_Not_Treated_As_Empty(string body)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Message_From_Another_Channel_Is_Rejected()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Messages(false, "11").Replace(ChannelId, "123"))));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Missing_Message_Fields_Or_Bad_Timestamp_Fail_Without_Content_In_Errors()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Messages(false, "11").Replace("2026-11-11T12:00:00.000Z", "sentinel-message-content"))));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
        Assert.DoesNotContain("sentinel-message-content", error.ToString());
    }

    [Fact]
    public async Task Incomplete_Dm_Enumeration_Returns_Failure_Instead_Of_Membership()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Dms("100"))));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http, Settings(maxPages: 1)).GetAllIndividualDmsAsync(UserId)).Value);
        Assert.Equal(FluxerFailure.PaginationLimitExceeded, error.Failure);
    }

    [Fact]
    public async Task A_Repeated_Or_Non_Progressing_Dm_Page_Is_Rejected()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Dms("100"))),
            (_, _) => Task.FromResult(Json(Dms("100"))));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetAllIndividualDmsAsync(UserId)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(401, FluxerFailure.Unauthorized)]
    [InlineData(403, FluxerFailure.Forbidden)]
    [InlineData(404, FluxerFailure.NotFound)]
    public async Task Authentication_And_Not_Found_Errors_Are_Not_Retried_Or_Treated_As_Empty(int status, FluxerFailure expected)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(
            $"{{\"code\":\"MISSING_ACL\",\"message\":\"{AdminKey}\"}}", (HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetAllIndividualDmsAsync(UserId)).Value);
        Assert.Equal(expected, error.Failure);
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.DoesNotContain(AdminKey, error.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Transient_Get_Errors_Have_A_Bounded_Retry_Count()
    {
        using var handler = new ScriptedHandler(Enumerable.Range(0, 3).Select<int, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(_ =>
            (_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable))).ToArray());
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http, Settings(maxDelay: TimeSpan.Zero)).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.UpstreamUnavailable, error.Failure);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Connection_Errors_Can_Retry_But_Tls_Errors_Do_Not()
    {
        using var handler = new ScriptedHandler((_, _) => throw new HttpRequestException(HttpRequestError.ConnectionError, OAuthToken),
            (_, _) => Task.FromResult(Json(Messages(false))));
        using var http = new HttpClient(handler);
        Assert.Empty(Success(await Admin(http, Settings(maxDelay: TimeSpan.Zero)).GetMessagesAsync(ChannelId)).Messages);
        Assert.Equal(2, handler.Requests.Count);

        using var tlsHandler = new ScriptedHandler((_, _) => throw new HttpRequestException(HttpRequestError.SecureConnectionError, AdminKey));
        using var tlsHttp = new HttpClient(tlsHandler);
        var error = Assert.IsType<FluxerError>((await Admin(tlsHttp).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.UpstreamUnavailable, error.Failure);
        Assert.DoesNotContain(AdminKey, error.ToString());
        Assert.Single(tlsHandler.Requests);
    }

    [Fact]
    public async Task Fractional_Rate_Limit_Delay_Is_Respected_Before_Retry()
    {
        var watch = Stopwatch.StartNew();
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(
            "{\"code\":\"RATE_LIMITED\",\"message\":\"limited\",\"global\":false,\"retry_after\":0.03}", HttpStatusCode.TooManyRequests)),
            (_, _) =>
            {
                Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(25));
                return Task.FromResult(Json(Messages(false)));
            });
        using var http = new HttpClient(handler);
        Assert.Empty(Success(await Admin(http).GetMessagesAsync(ChannelId)).Messages);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_After_Delta_Or_Date_Beyond_Budget_Is_Not_Shortened(bool date)
    {
        using var handler = new ScriptedHandler((_, _) =>
        {
            var response = Json("{\"retry_after\":0.001}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = date
                ? new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddMinutes(1))
                : new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.RateLimited, error.Failure);
        Assert.True(error.RetryAfter > TimeSpan.FromSeconds(30));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retry_After_Header_With_Non_Json_503_Body_Is_Respected()
    {
        using var handler = new ScriptedHandler((_, _) =>
        {
            var response = Json("service unavailable", HttpStatusCode.ServiceUnavailable);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.UpstreamUnavailable, error.Failure);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Rate_Limit_Without_A_Wait_Is_Not_Blindly_Retried()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json("{}", HttpStatusCode.TooManyRequests)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.RateLimited, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task Redirects_Never_Trigger_Another_Request(int status)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Headers = { Location = new Uri("https://attacker.example/collect") }
        }));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.RedirectRejected, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Caller_Cancellation_Stops_The_Request_And_Is_Not_A_Retry()
    {
        using var source = new CancellationTokenSource();
        using var handler = new ScriptedHandler(async (_, token) =>
        {
            source.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Admin(http).GetMessagesAsync(ChannelId, cancellationToken: source.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Request_Timeout_Is_Distinct_From_Caller_Cancellation()
    {
        using var handler = new ScriptedHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json("{}");
        });
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http, Settings(timeout: TimeSpan.FromMilliseconds(50))).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.Timeout, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Timeout_Also_Covers_Response_Streaming()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new ReadStream(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return 0;
            }))
        }));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http, Settings(timeout: TimeSpan.FromMilliseconds(50))).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.Timeout, error.Failure);
    }

    [Fact]
    public async Task Cancellation_Interrupts_Retry_Wait()
    {
        using var source = new CancellationTokenSource();
        using var handler = new ScriptedHandler((_, _) =>
        {
            source.CancelAfter(TimeSpan.FromMilliseconds(30));
            return Task.FromResult(Json("{\"retry_after\":5}", HttpStatusCode.TooManyRequests));
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Admin(http, Settings(maxDelay: TimeSpan.FromSeconds(5)))
            .GetMessagesAsync(ChannelId, cancellationToken: source.Token));
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Response_Size_Is_Bounded_With_Or_Without_Content_Length(bool unknownLength)
    {
        using var handler = new ScriptedHandler((_, _) =>
        {
            var response = Json(Messages(false));
            if (unknownLength)
                response.Content = new StreamContent(new ReadStream((buffer, _) =>
                {
                    buffer.Span.Fill((byte)'x');
                    return ValueTask.FromResult(buffer.Length);
                }));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http, Settings(maxBytes: 8)).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.ResponseTooLarge, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Identity_Uses_Only_The_Per_Request_Oauth_Token_And_Requires_Matching_String_Subject()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json($"{{\"id\":\"{UserId}\",\"sub\":\"{UserId}\"}}")));
        using var http = new HttpClient(handler);
        var result = Success(await Identity(http).GetIdentityAsync(OAuthToken));
        Assert.Equal(UserId, result.Id);
        Assert.Equal(result.Id, result.Sub);
        Assert.EndsWith("/api/prefix/v1/oauth2/userinfo", handler.Requests[0].Url);
        Assert.Equal($"Bearer {OAuthToken}", handler.Requests[0].Authorization);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"id\":\"123\",\"sub\":\"456\"}")]
    [InlineData("{\"id\":123,\"sub\":123}")]
    [InlineData("{\"id\":null,\"sub\":null}")]
    public async Task Malformed_Identity_Never_Establishes_A_Subject(string body)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(body)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Identity(http).GetIdentityAsync(OAuthToken)).Value);
        Assert.Equal(FluxerFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Credentials_Do_Not_Leak_Between_Admin_Oauth_Users_Or_Unrelated_Requests()
    {
        using var handler = new ScriptedHandler(
            (_, _) => Task.FromResult(Json(Dms())),
            (_, _) => Task.FromResult(Json($"{{\"id\":\"{UserId}\",\"sub\":\"{UserId}\"}}")),
            (_, _) => Task.FromResult(Json("{\"id\":\"123\",\"sub\":\"123\"}")),
            (_, _) => Task.FromResult(Json("{}")));
        // Sharing even this HttpClient must not leave any default Authorization behind.
        using var http = new HttpClient(handler);
        http.BaseAddress = new Uri("https://attacker.example/");
        await Admin(http).GetIndividualDmPageAsync(UserId);
        await Identity(http).GetIdentityAsync(OAuthToken);
        await Identity(http).GetIdentityAsync("another-user-token");
        using var unrelated = await http.GetAsync("https://unrelated.example/");
        Assert.Equal(new[] { $"Admin {AdminKey}", $"Bearer {OAuthToken}", "Bearer another-user-token", null },
            handler.Requests.Select(r => r.Authorization));
        Assert.All(handler.Requests.Take(3), request => Assert.StartsWith("https://fluxer.example/api/prefix/", request.Url));
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("1/../../other")]
    [InlineData("1?x=2")]
    [InlineData("01")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("18446744073709551616")]
    public async Task Invalid_Ids_Cannot_Change_The_Destination(string id)
    {
        using var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Admin(http).GetIndividualDmPageAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => Admin(http).GetMessagesAsync(id));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(101, null, null)]
    [InlineData(1, "1", "2")]
    [InlineData(1, "https://attacker.example", null)]
    public async Task Invalid_Paging_Is_Rejected_Before_Http(int limit, string? before, string? after)
    {
        using var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => Admin(http).GetMessagesAsync(ChannelId, new FluxerPageRequest(limit, before, after)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Zero_Is_Allowed_As_A_Cursor_But_Not_As_An_Identity()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Messages(false, "11"))));
        using var http = new HttpClient(handler);
        Assert.Single(Success(await Admin(http).GetMessagesAsync(ChannelId, new FluxerPageRequest(after: "0"))).Messages);
        Assert.EndsWith("&after=0", handler.Requests[0].Url);
    }

    [Fact]
    public async Task Failure_On_A_Later_Membership_Page_Never_Returns_Partial_Results()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json(Dms("100"))),
            (_, _) => Task.FromResult(Json("{\"code\":\"MISSING_ACL\",\"message\":\"denied\"}", HttpStatusCode.Forbidden)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetAllIndividualDmsAsync(UserId)).Value);
        Assert.Equal(FluxerFailure.Forbidden, error.Failure);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Persistent_Rate_Limits_Exhaust_The_Retry_Budget()
    {
        using var handler = new ScriptedHandler(Enumerable.Range(0, 3).Select<int, Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>>(_ =>
            (_, _) => Task.FromResult(Json("{\"retry_after\":0.001}", HttpStatusCode.TooManyRequests))).ToArray());
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.RateLimited, error.Failure);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Timeout_Budget_Also_Covers_Retry_Waits()
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json("{\"retry_after\":5}", HttpStatusCode.TooManyRequests)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Admin(http,
            Settings(timeout: TimeSpan.FromMilliseconds(50), maxDelay: TimeSpan.FromSeconds(5))).GetMessagesAsync(ChannelId)).Value);
        Assert.Equal(FluxerFailure.Timeout, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(401, FluxerFailure.Unauthorized)]
    [InlineData(403, FluxerFailure.Forbidden)]
    public async Task Identity_Authentication_Errors_Never_Return_An_Identity(int status, FluxerFailure expected)
    {
        using var handler = new ScriptedHandler((_, _) => Task.FromResult(Json("{\"code\":\"MISSING_OAUTH_SCOPE\",\"message\":\"denied\"}", (HttpStatusCode)status)));
        using var http = new HttpClient(handler);
        var error = Assert.IsType<FluxerError>((await Identity(http).GetIdentityAsync(OAuthToken)).Value);
        Assert.Equal(expected, error.Failure);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("token with spaces")]
    [InlineData("token\r\nInjected: value")]
    public async Task Invalid_Oauth_Credentials_Are_Rejected_Before_Http(string token)
    {
        using var handler = new ScriptedHandler();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Identity(http).GetIdentityAsync(token));
        Assert.Empty(handler.Requests);
    }

    private static T Success<T>(FluxerResult<T> result)
    {
        return Assert.IsAssignableFrom<T>(result.Value);
    }

    private sealed class ScriptedHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
        : HttpMessageHandler
    {
        public List<(string Url, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString()));
            Assert.True(Requests.Count <= steps.Length, "Unexpected extra HTTP request.");
            return steps[Requests.Count - 1](request, token);
        }
    }

    private sealed class ReadStream(Func<Memory<byte>, CancellationToken, ValueTask<int>> read) : Stream
    {
        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return read(buffer, cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
