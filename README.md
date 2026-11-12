# JJConsulting.Fluxer

.NET client and MCP server for Fluxer. The MCP reads the signed-in user's non-NSFW individual DMs through the Admin API.

## REST API Client
Typed .NET client for Fluxer's identity and Admin APIs, with dependency injection support.

```json
{
  "Fluxer": {
    "ApiBaseUrl": "https://your-fluxer-instance.com/api",
    "AdminApiKey": "YOUR_ADMIN_API_KEY"
  }
}
```

Register the clients and retrieve the signed-in user's DMs:

```csharp
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Client.Configuration;
using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddFluxerClient(builder.Configuration.GetSection("Fluxer"));
using var host = builder.Build();

var identityClient = host.Services.GetRequiredService<FluxerIdentityClient>();
var identityResult = await identityClient.GetIdentityAsync("USER_OAUTH_ACCESS_TOKEN");
if (identityResult is FluxerError identityError)
{
    Console.WriteLine($"Identity failed: {identityError.Failure}");
    return;
}
if (identityResult is not FluxerIdentity identity) 
    return;

var adminClient = host.Services.GetRequiredService<FluxerAdminClient>();
var dmResult = await adminClient.GetIndividualDmPageAsync(identity.Id);
if (dmResult is FluxerError dmError)
{
    Console.WriteLine($"DM lookup failed: {dmError.Failure}");
    return;
}
if (dmResult is FluxerDmPage page)
    foreach (var channel in page.Channels)
        Console.WriteLine(channel.ChannelId);
```

`ApiBaseUrl` must use HTTPS and omit `/v1`. Keep the Admin API key on the server; your application must authorize access to each user and channel. `FluxerAdminClient` also provides `GetMessagesAsync` and `SearchMessagesAsync`.


## MCP Server

Requires **.NET SDK 11.0.100**, a dedicated Fluxer OAuth application, and an Admin API key. The key and its owner need `user:list:dm_channels` and `message:lookup`; the owner also needs `admin:authenticate`.

Create `src/JJConsulting.Fluxer.Mcp/appsettings.Development.json` (git-ignored):

```json
{
  "AllowedHosts": "localhost;127.0.0.1;[::1];YOUR_PUBLIC_HOST",
  "Fluxer": {
    "ApiBaseUrl": "https://your-fluxer-instance.com/api",
    "ClientId": "YOUR_CLIENT_ID",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "AdminApiKey": "YOUR_ADMIN_API_KEY",
    "PublicUrl": "https://YOUR_PUBLIC_HOST",
    "ClientRedirectUri": "https://chatgpt.com/connector_platform_oauth_redirect"
  }
}
```

`ApiBaseUrl` must use HTTPS and omit `/v1` and the trailing slash. `PublicUrl` is the public HTTPS origin, without a path or trailing slash. Keep credentials on the server and out of Git.

```bash
dotnet dev-certs https --trust
dotnet run --project src/JJConsulting.Fluxer.Mcp --launch-profile https
```

For local development with ChatGPT, expose the server in another terminal:

```bash
ngrok http https://localhost:7042
```

Use the tunnel hostname in `AllowedHosts` and its HTTPS origin in `PublicUrl`. Restart the server after configuration changes. Keep both processes running.

## Connect ChatGPT

1. In the Fluxer OAuth application, register `https://YOUR_PUBLIC_HOST/auth/fluxer/callback`.
2. Create a custom MCP app in ChatGPT (enable developer mode if required):

   | Setting | Value |
   | --- | --- |
   | MCP server URL | `https://YOUR_PUBLIC_HOST/mcp` |
   | Authentication | OAuth |
   | Client mode | Pre-defined |
   | Client ID | Same as `Fluxer:ClientId` |
   | Client Secret | Empty |
   | Token endpoint authentication | None |
   | Registration URL | Empty |
   | Scope | `identify` |

3. Set `Fluxer:ClientRedirectUri` to the **exact callback displayed by ChatGPT** and restart the server if it changes. This differs from the Fluxer callback in step 1.
4. Connect, sign in to Fluxer, and select the app in a conversation.

If the tunnel hostname changes, update `AllowedHosts`, `PublicUrl`, the Fluxer callback, and the MCP URL in ChatGPT. Refresh ChatGPT's tool list after server tool changes.

Connection failing? Check that public `/health` and `/.well-known/oauth-authorization-server` return HTTP `200`, and that the metadata's `issuer` matches `PublicUrl`.

## Tools

| Tool | Purpose |
| --- | --- |
| `get_profile` | Get your Fluxer user ID. |
| `list_my_dm_channels` | Page through your DMs. |
| `get_my_recent_conversations` | Get recently active DMs and message previews. |
| `find_my_dm_contacts` | Find a DM by contact name, username, or ID. |
| `get_my_dm_messages` | Read a DM, optionally filtering by date. |
| `get_my_dm_activity` | Read messages across your DMs for a period. |
| `search_my_dm_messages` | Search text in one DM. |

- Dates require ISO-8601 timestamps with a UTC offset. For a day in São Paulo: `start: "2026-11-11T00:00:00-03:00"`, `end: "2026-11-12T00:00:00-03:00"`. Start is inclusive; end is exclusive.
- Activity returns up to 100 messages per channel. Continue with `NextChannelBefore` for more channels; use `get_my_dm_messages` with the same dates and each conversation's `NextBefore` while `HasMore` is true.
- Search returns at most 100 matches (`Truncated` indicates more). Empty results may mean the instance has no search index.
- Run one MCP process: pending OAuth logins are stored in memory and lost on restart.


