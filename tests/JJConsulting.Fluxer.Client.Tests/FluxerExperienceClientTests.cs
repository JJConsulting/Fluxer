using System.Net;
using System.Text;
using System.Text.Json;
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client.Tests;

public sealed class FluxerExperienceClientTests
{
    private sealed class Reply(string body) : HttpMessageHandler
    {
        public Uri? Destination;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Destination = request.RequestUri;
            Assert.Equal("Admin fixture-admin", request.Headers.Authorization!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static FluxerAdminClient Client(HttpClient http) => new(http, Options.Create(new FluxerClientOptions
    {
        ApiBaseUrl = "https://fluxer.test/api", AdminApiKey = "fixture-admin"
    }));

    [Fact]
    public async Task Recipient_Names_And_Last_Message_Id_Are_Read_From_The_Existing_Dm_Endpoint()
    {
        using var reply = new Reply("""
            {"channels":[{"channel_id":"1000","channel_type":1,"channel_nsfw":false,
            "recipient_ids":["456","789"],"recipients":[{"id":"789","username":"gabriel","global_name":"Gabriel Test"}],
            "last_message_id":"2000"}]}
            """);
        using var http = new HttpClient(reply);
        var result = await Client(http).GetIndividualDmPageAsync("456");
        var page = Assert.IsType<FluxerDmPage>(result.Value);
        Assert.Equal("Gabriel Test", page.Channels[0].Recipients[0].GlobalName);
        Assert.Equal("2000", page.Channels[0].LastMessageId);
    }

    [Theory]
    [InlineData("{\"id\":\"999\",\"username\":\"stranger\"}")]
    [InlineData("{\"id\":\"789\"}")]
    [InlineData("{\"id\":\"789\",\"username\":\"name\"},{\"id\":\"789\",\"username\":\"duplicate\"}")]
    public async Task Invalid_Or_Unrelated_Recipient_Profiles_Are_Rejected(string recipients)
    {
        using var reply = new Reply("{\"channels\":[{\"channel_id\":\"1000\",\"channel_type\":1," +
            "\"recipient_ids\":[\"456\",\"789\"],\"recipients\":[" + recipients + "]}]}");
        using var http = new HttpClient(reply);
        var result = await Client(http).GetIndividualDmPageAsync("456");
        Assert.Equal(FluxerFailure.InvalidResponse, Assert.IsType<FluxerError>(result.Value).Failure);
    }

    [Fact]
    public async Task Search_Query_Is_Encoded_And_Author_Names_Are_Preserved()
    {
        using var reply = new Reply("""
            {"messages":[{"id":"2000","channel_id":"1000","author_id":"789","author_username":"gabriel",
            "author_global_name":"Gabriel Test","content":"fixture","timestamp":"2026-11-11T10:00:00Z"}],"total":7}
            """);
        using var http = new HttpClient(reply);
        var result = await Client(http).SearchMessagesAsync("1000", "Azure & amanhã?", 10);
        var search = Assert.IsType<FluxerMessageSearch>(result.Value);
        Assert.Equal("Gabriel Test", search.Messages[0].AuthorGlobalName);
        Assert.Equal(7, search.Total);
        Assert.Equal("/api/v1/admin/messages", reply.Destination!.AbsolutePath);
        Assert.Contains("q=Azure%20%26%20amanh%C3%A3%3F", reply.Destination.Query);
        Assert.Contains("channel_id=1000", reply.Destination.Query);
    }

    [Theory]
    [InlineData("8888", 1)]
    [InlineData("1000", -1)]
    [InlineData("1000", 0)]
    public async Task Search_Rejects_Unrelated_Channels_And_Invalid_Totals(string channel, int total)
    {
        using var reply = new Reply(JsonSerializer.Serialize(new
        {
            messages = new[] { new { id = "2000", channel_id = channel, author_id = "789", content = "fixture",
                timestamp = "2026-11-11T10:00:00Z" } }, total
        }));
        using var http = new HttpClient(reply);
        var result = await Client(http).SearchMessagesAsync("1000", "query");
        Assert.Equal(FluxerFailure.InvalidResponse, Assert.IsType<FluxerError>(result.Value).Failure);
    }

    [Theory]
    [InlineData("", 25)]
    [InlineData("  ", 25)]
    [InlineData("query", 0)]
    [InlineData("query", 101)]
    public async Task Invalid_Search_Does_Not_Send_Any_Request(string query, int limit)
    {
        using var reply = new Reply("{}");
        using var http = new HttpClient(reply);
        await Assert.ThrowsAsync<ArgumentException>(() => Client(http).SearchMessagesAsync("1000", query, limit));
        Assert.Null(reply.Destination);
    }
}
