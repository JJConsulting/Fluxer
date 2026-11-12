using System.Net;
using System.Text.Json;

namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class DmExperienceTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-11T00:00:00-03:00");
    private static readonly DateTimeOffset End = Start.AddDays(1);

    private static async Task<JsonDocument> Call(HttpClient client, string tool, object arguments, string token = "first-user-token")
    {
        using var response = await McpFactory.ToolAsync(client, token, tool, arguments);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var payload = body.StartsWith("data:") || body.StartsWith("event:")
            ? body.Split('\n').First(line => line.StartsWith("data:")).Substring(5).Trim() : body;
        return JsonDocument.Parse(payload);
    }

    private static JsonElement Content(JsonDocument json)
    {
        var result = json.RootElement.GetProperty("result");
        Assert.False(result.TryGetProperty("isError", out var error) && error.GetBoolean(), result.ToString());
        return result.GetProperty("structuredContent");
    }

    [Fact]
    public async Task Existing_Tools_Include_Participant_And_Author_Names()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var channels = await Call(client, "list_my_dm_channels", new { });
        var contact = Content(channels).GetProperty("channels")[0];
        Assert.Equal("gabriel", contact.GetProperty("username").GetString());
        Assert.Equal("Gabriel Test", contact.GetProperty("displayName").GetString());
        using var messages = await Call(client, "get_my_dm_messages", new { channelId = "1000" });
        Assert.Equal("Gabriel Test", Content(messages).GetProperty("messages")[0].GetProperty("authorName").GetString());
    }

    [Fact]
    public async Task Missing_Names_Keep_Stable_Ids_Without_Inventing_An_Identity()
    {
        using var factory = new McpFactory();
        factory.Fluxer.IncludeNames = false;
        using var client = factory.Browser();
        using var channels = await Call(client, "list_my_dm_channels", new { });
        var contact = Content(channels).GetProperty("channels")[0];
        Assert.Equal("789", contact.GetProperty("otherUserId").GetString());
        Assert.True(!contact.TryGetProperty("displayName", out var name) || name.ValueKind == JsonValueKind.Null);
    }

    [Theory]
    [InlineData("GABRIEL", 1)]
    [InlineData("Test", 1)]
    [InlineData("789", 1)]
    [InlineData("stranger", 0)]
    public async Task Contact_Search_Uses_Only_The_Authenticated_Users_Dms(string query, int expected)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await Call(client, "find_my_dm_contacts", new { query });
        Assert.Equal(expected, Content(response).GetProperty("total").GetInt32());
        Assert.All(factory.Fluxer.DmUsers, id => Assert.Equal("456", id));
        Assert.Equal(0, factory.Fluxer.MessageReads);
        Assert.Equal(0, factory.Fluxer.SearchReads);
    }

    [Fact]
    public async Task Recent_Conversations_Use_Last_Message_Order_Instead_Of_Channel_Creation_Order()
    {
        using var factory = new McpFactory();
        factory.Fluxer.TwoChannels = true;
        using var client = factory.Browser();
        using var response = await Call(client, "get_my_recent_conversations", new { limit = 1 });
        var content = Content(response);
        Assert.Equal(2, content.GetProperty("total").GetInt32());
        var conversation = content.GetProperty("conversations")[0];
        Assert.Equal("999", conversation.GetProperty("channel").GetProperty("id").GetString());
        Assert.Equal("Gabriel Test", conversation.GetProperty("channel").GetProperty("displayName").GetString());
        Assert.Equal(1, factory.Fluxer.MessageReads);
        Assert.Equal("999", Assert.Single(factory.Fluxer.ReadChannels));
    }

    [Fact]
    public async Task Contact_Limits_Expose_Truncation()
    {
        using var factory = new McpFactory();
        factory.Fluxer.TwoChannels = true;
        using var client = factory.Browser();
        using var response = await Call(client, "find_my_dm_contacts", new { query = "Gabriel", limit = 1 });
        var content = Content(response);
        Assert.Equal(2, content.GetProperty("total").GetInt32());
        Assert.True(content.GetProperty("truncated").GetBoolean());
        Assert.Single(content.GetProperty("contacts").EnumerateArray());
    }

    [Fact]
    public async Task Activity_Returns_Period_Evidence_With_Names_And_Explicit_Channel_Pagination()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await Call(client, "get_my_dm_activity", new { start = Start, end = End });
        var content = Content(response);
        var conversation = content.GetProperty("conversations")[0];
        Assert.Equal("Gabriel Test", conversation.GetProperty("channel").GetProperty("displayName").GetString());
        Assert.Single(conversation.GetProperty("messages").GetProperty("messages").EnumerateArray());
        Assert.Equal(((End.ToUnixTimeMilliseconds() - 1420070400000L) << 22).ToString(), factory.Fluxer.LastMessageBefore);
        Assert.Equal("1000", content.GetProperty("nextChannelBefore").GetString());
        using var next = await Call(client, "get_my_dm_activity", new { start = Start, end = End, before = "1000" });
        Assert.Equal(0, Content(next).GetProperty("scannedChannels").GetInt32());
        Assert.True(!Content(next).TryGetProperty("nextChannelBefore", out var cursor) || cursor.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task Period_Filter_Is_Start_Inclusive_And_End_Exclusive()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        var timestamp = factory.Fluxer.MessageTime;
        using var included = await Call(client, "get_my_dm_messages", new { channelId = "1000", start = timestamp, end = timestamp.AddHours(1) });
        Assert.Single(Content(included).GetProperty("messages").EnumerateArray());
        using var excluded = await Call(client, "get_my_dm_messages", new { channelId = "1000", start = timestamp.AddHours(-1), end = timestamp });
        Assert.Empty(Content(excluded).GetProperty("messages").EnumerateArray());
    }

    [Fact]
    public async Task Submillisecond_End_Does_Not_Drop_The_Last_Included_Millisecond()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        var end = factory.Fluxer.MessageTime.AddTicks(1);
        using var response = await Call(client, "get_my_dm_messages", new { channelId = "1000", start = Start, end });
        Assert.Single(Content(response).GetProperty("messages").EnumerateArray());
        Assert.Equal((((end.ToUnixTimeMilliseconds() + 1) - 1420070400000L) << 22).ToString(), factory.Fluxer.LastMessageBefore);
    }

    [Fact]
    public async Task Activity_Exposes_Message_Truncation_And_Stops_Once_The_Start_Is_Crossed()
    {
        using var factory = new McpFactory();
        factory.Fluxer.MoreMessages = true;
        using var client = factory.Browser();
        using var partial = await Call(client, "get_my_dm_activity", new { start = Start, end = End });
        var messages = Content(partial).GetProperty("conversations")[0].GetProperty("messages");
        Assert.True(messages.GetProperty("hasMore").GetBoolean());
        Assert.Equal("2000", messages.GetProperty("nextBefore").GetString());
        factory.Fluxer.MessageTime = Start.AddHours(-1);
        using var older = await Call(client, "get_my_dm_activity", new { start = Start, end = End });
        Assert.Empty(Content(older).GetProperty("conversations").EnumerateArray());
    }

    [Fact]
    public async Task Text_Search_Encodes_The_Query_And_Reports_The_Upstream_Total()
    {
        using var factory = new McpFactory();
        factory.Fluxer.SearchTotal = 7;
        using var client = factory.Browser();
        const string query = "Azure & amanhã?";
        using var response = await Call(client, "search_my_dm_messages", new { channelId = "1000", query });
        var content = Content(response);
        Assert.Equal(7, content.GetProperty("total").GetInt32());
        Assert.True(content.GetProperty("truncated").GetBoolean());
        Assert.Equal("Gabriel Test", content.GetProperty("messages")[0].GetProperty("authorName").GetString());
        Assert.Equal(query, factory.Fluxer.SearchQuery);
    }

    [Theory]
    [InlineData("get_my_dm_messages")]
    [InlineData("search_my_dm_messages")]
    public async Task Channel_Tools_Reject_Other_Accounts_Before_Reading(string tool)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var denied = await Call(client, tool, new { channelId = "1001", query = "Azure" });
        Assert.True(denied.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal(0, factory.Fluxer.MessageReads);
        Assert.Equal(0, factory.Fluxer.SearchReads);
    }

    [Theory]
    [InlineData("find_my_dm_contacts")]
    [InlineData("get_my_recent_conversations")]
    [InlineData("get_my_dm_activity")]
    public async Task Cross_Conversation_Tools_Stay_In_The_Logged_In_Account(string tool)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await Call(client, tool, new { query = "Gabriel", start = Start, end = End }, "second-user-token");
        var content = Content(response).ToString();
        Assert.Contains("1001", content);
        Assert.DoesNotContain("1000", content);
        Assert.All(factory.Fluxer.DmUsers, id => Assert.Equal("567", id));
        Assert.All(factory.Fluxer.ReadChannels, id => Assert.Equal("1001", id));
    }

    [Fact]
    public async Task A_Search_Response_From_Another_Channel_Is_Rejected()
    {
        using var factory = new McpFactory();
        factory.Fluxer.SearchWrongChannel = true;
        using var client = factory.Browser();
        using var response = await Call(client, "search_my_dm_messages", new { channelId = "1000", query = "Azure" });
        Assert.True(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.DoesNotContain("fixture message", response.ToString());
    }

    [Theory]
    [InlineData("find_my_dm_contacts", "", 20)]
    [InlineData("get_my_recent_conversations", "test", 21)]
    [InlineData("search_my_dm_messages", "", 25)]
    [InlineData("get_my_dm_activity", "test", 21)]
    public async Task Invalid_Tool_Arguments_Do_Not_Reach_Admin(string tool, string query, int limit)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await Call(client, tool, new { channelId = "1000", query, limit, start = Start, end = End });
        Assert.True(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Empty(factory.Fluxer.DmUsers);
        Assert.Empty(factory.Fluxer.ReadChannels);
    }

    [Fact]
    public async Task Invalid_Date_Ranges_Do_Not_Reach_Admin()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        foreach (var (start, end) in new[] { (End, Start), (Start, Start), (Start, DateTimeOffset.MaxValue) })
        {
            using var response = await Call(client, "get_my_dm_activity", new { start, end });
            Assert.True(response.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        }
        Assert.Empty(factory.Fluxer.DmUsers);
    }
}
