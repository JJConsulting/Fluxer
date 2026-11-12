using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JJConsulting.Fluxer.Mcp.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Upstream_Access_Token_Authenticates_Without_A_Local_Login_Or_Database()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("456", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Fluxer.Introspections);
        Assert.Empty(factory.Fluxer.DmUsers);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("client_id")]
    [InlineData("scope")]
    [InlineData("token_type")]
    [InlineData("sub")]
    [InlineData("exp")]
    [InlineData("malformed")]
    [InlineData("inactive_only")]
    [InlineData("invalid_json")]
    [InlineData("wrong_types")]
    [InlineData("oversized")]
    [InlineData("redirect")]
    [InlineData("unavailable")]
    [InlineData("transport")]
    public async Task Invalid_Or_Unverifiable_Tokens_Never_Reach_Admin(string scenario)
    {
        using var factory = new McpFactory();
        switch (scenario)
        {
            case "active": factory.Fluxer.Claims[scenario] = false; break;
            case "exp": factory.Fluxer.Claims[scenario] = 0; break;
            case "sub": factory.Fluxer.Claims[scenario] = "../../other-user"; break;
            case "malformed": factory.Fluxer.IntrospectionBody = "{}"; break;
            case "inactive_only": factory.Fluxer.IntrospectionBody = "{\"active\":false}"; break;
            case "invalid_json": factory.Fluxer.IntrospectionBody = "not json"; break;
            case "wrong_types": factory.Fluxer.Claims["exp"] = "invalid"; break;
            case "oversized": factory.Fluxer.IntrospectionBody = new string('x', 65537); break;
            case "redirect": factory.Fluxer.IntrospectionStatus = HttpStatusCode.Redirect; break;
            case "unavailable": factory.Fluxer.IntrospectionStatus = HttpStatusCode.ServiceUnavailable; break;
            case "transport": factory.Fluxer.ThrowOnIntrospection = true; break;
            default: factory.Fluxer.Claims[scenario] = "other"; break;
        }
        using var client = factory.Browser();
        using var response = await McpFactory.ToolAsync(client, "first-user-token", "list_my_dm_channels");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal(1, factory.Fluxer.Introspections);
        Assert.Empty(factory.Fluxer.DmUsers);
        Assert.Equal(0, factory.Fluxer.MessageReads);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task Anonymous_Mcp_Requests_Are_Challenged(string method)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/mcp"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.Introspections);
    }

    [Theory]
    [InlineData("Basic fixture-admin")]
    [InlineData("Bearer")]
    [InlineData("Bearer token with spaces")]
    public async Task Invalid_Authorization_Header_Never_Calls_Fluxer(string header)
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", header);
        using var response = await McpFactory.ToolAsync(client, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.Introspections);
    }

    [Fact]
    public async Task Query_And_Cookie_Credentials_Are_Not_Accepted()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        client.DefaultRequestHeaders.Add("Cookie", "access_token=first-user-token");
        using var response = await client.GetAsync("/mcp?access_token=first-user-token");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.Introspections);
    }

    [Fact]
    public async Task Revocation_Takes_Effect_On_The_Next_Request()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var first = await McpFactory.ToolAsync(client, "first-user-token");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        factory.Fluxer.Claims["active"] = false;
        using var revoked = await McpFactory.ToolAsync(client, "first-user-token");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
    }

    [Fact]
    public async Task Discovery_Points_To_Fluxer_And_Does_Not_Expose_Secrets()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var resource = await client.GetAsync("/.well-known/oauth-protected-resource");
        using var prm = JsonDocument.Parse(await resource.Content.ReadAsStringAsync());
        Assert.Equal(McpFactory.Origin + "/mcp", prm.RootElement.GetProperty("resource").GetString());
        using var metadata = await client.GetAsync("/.well-known/oauth-authorization-server");
        var body = await metadata.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(McpFactory.Origin + "/oauth/authorize", json.RootElement.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(McpFactory.Origin + "/oauth/token", json.RootElement.GetProperty("token_endpoint").GetString());
        Assert.Contains("S256", body);
        Assert.Equal("none", json.RootElement.GetProperty("token_endpoint_auth_methods_supported")[0].GetString());
        Assert.DoesNotContain("fixture-secret", body);
        Assert.DoesNotContain("fixture-admin", body);
        using var invalidTokenRequest = await client.PostAsync("/oauth/token", null);
        Assert.Equal(HttpStatusCode.BadRequest, invalidTokenRequest.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://public.example.com")]
    public async Task Public_Url_Is_Inferred_Unless_Explicitly_Configured(string? publicUrl)
    {
        using var factory = new McpFactory(publicUrl);
        using var client = factory.Browser();
        client.BaseAddress = new Uri("https://mcp.test:7443");
        var expected = string.IsNullOrEmpty(publicUrl) ? "https://mcp.test:7443" : publicUrl;
        using var resource = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(HttpStatusCode.OK, resource.StatusCode);
        using var prm = JsonDocument.Parse(await resource.Content.ReadAsStringAsync());
        Assert.Equal(expected + "/mcp", prm.RootElement.GetProperty("resource").GetString());
        Assert.Equal(expected, prm.RootElement.GetProperty("authorization_servers")[0].GetString());
        using var metadata = await client.GetAsync("/.well-known/oauth-authorization-server");
        using var server = JsonDocument.Parse(await metadata.Content.ReadAsStringAsync());
        Assert.Equal(expected, server.RootElement.GetProperty("issuer").GetString());
        Assert.True(server.RootElement.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        using var challenge = await client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.Unauthorized, challenge.StatusCode);
        Assert.Contains(expected + "/.well-known/oauth-protected-resource", challenge.Headers.WwwAuthenticate.ToString());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.BadRequest)]
    [InlineData("https://public.example.com", HttpStatusCode.OK)]
    public async Task Http_Requires_An_Explicit_Https_Public_Url(string? publicUrl, HttpStatusCode expected)
    {
        using var factory = new McpFactory(publicUrl);
        using var client = factory.Browser();
        client.BaseAddress = new Uri("http://mcp.test");
        using var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Unapproved_Hosts_Are_Rejected_Before_Discovery()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        client.DefaultRequestHeaders.Host = "unapproved.test";
        using var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("http://mcp.test")]
    [InlineData("https://mcp.test/path")]
    [InlineData("https://mcp.test/")]
    [InlineData("https://user:password@mcp.test")]
    [InlineData("invalid")]
    public void Invalid_Public_Url_Overrides_Fail_At_Startup(string publicUrl)
    {
        using var factory = new McpFactory(publicUrl);
        Assert.Throws<InvalidOperationException>(() => factory.Browser());
    }

    [Fact]
    public void Missing_Configuration_Fails_At_Startup()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Fluxer:ApiBaseUrl", ""));
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    [Fact]
    public async Task Health_Does_Not_Need_OAuth_Or_Admin_Requests()
    {
        using var factory = new McpFactory();
        using var client = factory.Browser();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, factory.Fluxer.Introspections);
        Assert.Empty(factory.Fluxer.DmUsers);
    }
}
