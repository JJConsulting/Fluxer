using System.Net;

namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class DmAuthorizationTests
{
    [Fact]
    public async Task Admin_Uses_Only_The_Verified_User_Even_When_Arguments_Try_To_Override_It()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "get_my_dm_messages",
            new { channelId = "1000", user_id = "999" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("fixture message", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Fluxer.MessageReads);
        Assert.NotEmpty(factory.Fluxer.DmUsers);
        Assert.All(factory.Fluxer.DmUsers, id => Assert.Equal("456", id));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("group")]
    [InlineData("notes")]
    [InlineData("membership")]
    [InlineData("nsfw")]
    [InlineData("unknown_nsfw")]
    public async Task Unverified_Channel_Access_Never_Requests_Messages(string scenario)
    {
        using var factory = new McpFactory();
        switch (scenario)
        {
            case "group": factory.Fluxer.ChannelType = 3; break;
            case "notes": factory.Fluxer.ChannelType = 6; break;
            case "membership": factory.Fluxer.Recipients = ["777", "789"]; break;
            case "nsfw": factory.Fluxer.Nsfw = true; break;
            case "unknown_nsfw": factory.Fluxer.Nsfw = null; break;
        }
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "get_my_dm_messages",
            new { channelId = scenario == "unknown" ? "999" : "1000" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"isError\":true", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Fluxer.MessageReads);
    }

    [Fact]
    public async Task Two_Accounts_Cannot_Read_Each_Others_Channels()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        foreach (var (token, own, other) in new[]
        {
            ("first-user-token", "1000", "1001"), ("second-user-token", "1001", "1000")
        })
        {
            using var listing = await McpFactory.ToolAsync(client, token, "list_my_dm_channels");
            var body = await listing.Content.ReadAsStringAsync();
            Assert.Contains(own, body);
            Assert.DoesNotContain(other, body);
            using var denied = await McpFactory.ToolAsync(client, token, "get_my_dm_messages", new { channelId = other });
            Assert.Contains("\"isError\":true", await denied.Content.ReadAsStringAsync());
            using var allowed = await McpFactory.ToolAsync(client, token, "get_my_dm_messages", new { channelId = own });
            Assert.Contains("fixture message", await allowed.Content.ReadAsStringAsync());
        }
        Assert.Equal(2, factory.Fluxer.MessageReads);
    }

    [Fact]
    public async Task Membership_On_A_Later_Page_Is_Checked_Before_Message_Access()
    {
        using var factory = new McpFactory();
        factory.Fluxer.LaterPage = true;
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "get_my_dm_messages",
            new { channelId = "1000" });
        Assert.Contains("fixture message", await response.Content.ReadAsStringAsync());
        Assert.Equal(3, factory.Fluxer.DmUsers.Count);
        Assert.Equal(1, factory.Fluxer.MessageReads);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(201, null)]
    [InlineData(50, "../other")]
    public async Task Invalid_Pagination_Does_Not_Reach_Admin(int limit, string? before)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "list_my_dm_channels", new { limit, before });
        Assert.Contains("\"isError\":true", await response.Content.ReadAsStringAsync());
        Assert.Empty(factory.Fluxer.DmUsers);
    }

    [Fact]
    public async Task Logs_And_Errors_Do_Not_Expose_Credentials_Messages_Or_Upstream_Errors()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var allowed = await McpFactory.ToolAsync(client, "first-user-token", "get_my_dm_messages", new { channelId = "1000" });
        Assert.Contains("fixture message", await allowed.Content.ReadAsStringAsync());
        factory.Fluxer.FailMessages = true;
        using var failed = await McpFactory.ToolAsync(client, "first-user-token", "get_my_dm_messages", new { channelId = "1000" });
        var body = await failed.Content.ReadAsStringAsync();
        Assert.Contains("\"isError\":true", body);
        Assert.DoesNotContain("upstream-secret", body);
        var logs = string.Join('\n', factory.Logs);
        foreach (var secret in new[] { "first-user-token", "fixture-admin", "fixture-secret", "fixture message", "upstream-secret" })
            Assert.DoesNotContain(secret, logs);
    }
}
