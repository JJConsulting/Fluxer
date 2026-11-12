using System.Security.Claims;
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    [InlineData("456", "66560", "0", "0", false, false, true, null, true, true)]
    [InlineData("789", "66560", "0", "0", false, false, true, null, true, true)]
    [InlineData("789", "66560", "1024", "0", false, false, false, null, true, true)]
    [InlineData("789", "0", "0", "66560", false, false, true, null, true, true)]
    [InlineData("789", "8", "66560", "0", false, false, true, null, true, true)]
    [InlineData("789", "66560", "0", "0", true, false, false, null, true, true)]
    [InlineData("789", "66560", "0", "0", false, true, false, null, true, true)]
    [InlineData("789", "1024", "0", "0", false, false, false, null, true, true)]
    [InlineData("789", "1024", "0", "0", false, false, true, -60, true, true)]
    [InlineData("789", "1024", "0", "0", false, false, true, 60, false, true)]
    [InlineData("789", "66560", "0", "0", false, false, false, null, true, false)]
    public async Task Members_Receive_All_Authors_Only_In_Readable_Channels(string authorId, string permissions,
        string deny, string allow, bool hidden, bool fail, bool readable, int? cutoffSeconds, bool messageVisible, bool grantScope)
    {
        using var factory = new McpFactory();
        factory.Fluxer.GuildOwner = "567";
        factory.Fluxer.MessageAuthor = authorId;
        factory.Fluxer.GuildPermissions = permissions;
        factory.Fluxer.ChannelDeny = deny;
        factory.Fluxer.ChannelAllow = allow;
        factory.Fluxer.HiddenGuildChannel = hidden;
        factory.Fluxer.FailGuildPermissions = fail;
        factory.Fluxer.MoreMessages = true;
        factory.Fluxer.HistoryCutoff = cutoffSeconds is { } seconds ? factory.Fluxer.MessageTime.AddSeconds(seconds) : null;
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "456"), new Claim("scope", "identify"), new Claim("scope", "guilds")], "test"))
            }
        };
        if (!grantScope)
            ((ClaimsIdentity)accessor.HttpContext.User.Identity!).RemoveClaim(
                accessor.HttpContext.User.FindAll("scope").Single(claim => claim.Value == "guilds"));
        accessor.HttpContext.Request.Headers.Authorization = "Bearer first-user-token";
        var tools = new GuildTools(accessor,
            factory.Services.GetRequiredService<IOptions<FluxerClientOptions>>(),
            factory.Services.GetRequiredService<FluxerAdminClient>());
        if (!readable)
        {
            await Assert.ThrowsAsync<ModelContextProtocol.McpException>(() => tools.MessagesAsync("3000", "4000"));
            Assert.Equal(0, factory.Fluxer.MessageReads);
            return;
        }
        var channels = await tools.ChannelsAsync("3000");
        Assert.Contains(channels.Channels, channel => channel.Id == "4000");
        var messages = await tools.MessagesAsync("3000", "4000");
        Assert.Equal(messageVisible ? 1 : 0, messages.Messages.Count);
        Assert.All(messages.Messages, message => Assert.Equal(authorId, message.AuthorId));
        Assert.True(messages.HasMore);
        Assert.Equal("2000", messages.NextBefore);
        Assert.Equal(1, factory.Fluxer.MessageReads);
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
