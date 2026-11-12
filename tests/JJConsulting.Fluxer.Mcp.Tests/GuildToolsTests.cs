namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class GuildToolsTests
{
    [Fact]
    public async Task Lists_Guilds_And_Channels_Then_Reads_With_Pagination()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var guilds = await McpFactory.ToolAsync(client, "first-user-token", "list_my_guilds");
        Assert.Contains("Fixture guild", await guilds.Content.ReadAsStringAsync());
        using var next = await McpFactory.ToolAsync(client, "first-user-token", "list_my_guilds", new { after = "3000" });
        Assert.DoesNotContain("Fixture guild", await next.Content.ReadAsStringAsync());
        using var channels = await McpFactory.ToolAsync(client, "first-user-token", "list_my_guild_channels", new { guildId = "3000" });
        Assert.Contains("general", await channels.Content.ReadAsStringAsync());
        factory.Fluxer.MoreMessages = true;
        using var messages = await McpFactory.ToolAsync(client, "first-user-token", "get_my_guild_messages",
            new { guildId = "3000", channelId = "4000", before = "3000", userId = "999" });
        var body = await messages.Content.ReadAsStringAsync();
        Assert.Contains("fixture message", body);
        Assert.Contains("2000", body);
        Assert.DoesNotContain("\"isError\":true", body);
        Assert.Equal("3000", factory.Fluxer.LastMessageBefore);
        Assert.Equal(["4000"], factory.Fluxer.ReadChannels);
    }

    [Theory]
    [InlineData("list_my_dm_channels", false)]
    [InlineData("list_my_guilds", true)]
    public async Task Zero_Cursor_Keeps_Each_Tools_Existing_Pagination_Rules(string tool, bool isError)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", tool,
            new { before = "0", after = "0" });
        Assert.Equal(isError, (await response.Content.ReadAsStringAsync()).Contains("\"isError\":true"));
    }

    [Theory]
    [InlineData("non_member")]
    [InlineData("not_owner")]
    [InlineData("guild_nsfw")]
    [InlineData("channel_nsfw")]
    [InlineData("category")]
    [InlineData("wrong_channel")]
    [InlineData("invalid_id")]
    [InlineData("invalid_cursor")]
    [InlineData("invalid_limit")]
    [InlineData("upstream_failure")]
    public async Task Unverified_Access_Never_Reads_Messages(string scenario)
    {
        using var factory = new McpFactory();
        switch (scenario)
        {
            case "not_owner": factory.Fluxer.GuildOwner = "567"; break;
            case "guild_nsfw": factory.Fluxer.GuildNsfw = true; break;
            case "channel_nsfw": factory.Fluxer.GuildChannelNsfw = true; break;
            case "category": factory.Fluxer.GuildChannelType = 4; break;
            case "upstream_failure": factory.Fluxer.FailGuilds = true; break;
        }
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "get_my_guild_messages", new
        {
            guildId = scenario == "non_member" ? "9999" : scenario == "invalid_id" ? "../3000" : "3000",
            channelId = scenario == "wrong_channel" ? "9999" : "4000",
            before = scenario == "invalid_cursor" ? "../3000" : null,
            limit = scenario == "invalid_limit" ? 101 : 50
        });
        Assert.Contains("\"isError\":true", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, factory.Fluxer.MessageReads);
    }
}
