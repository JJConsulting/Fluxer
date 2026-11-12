# JJConsulting.Fluxer

This repository provides **two integrations with Fluxer**:

| Integration | Project | Use case |
| --- | --- | --- |
| **REST API Client** | `JJConsulting.Fluxer.Client` | Call Fluxer's identity and Admin REST APIs directly from your .NET application. Can be used independently of the MCP server. |
| **MCP Server** | `JJConsulting.Fluxer.Mcp` | Connect AI assistants such as ChatGPT to Fluxer through MCP tools. Uses the REST client internally to read the signed-in user's non-NSFW individual DMs. |

Use the REST client to integrate Fluxer into your application, or run the MCP server to expose Fluxer tools to an AI assistant. Setup instructions for each integration follow below.

## REST API Client

```bash
dotnet add package JJConsulting.Fluxer.Client --version 0.1.0
```

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

Release tags (`v*`) publish the Docker image to `ghcr.io/jjconsulting/fluxer-mcp:<version>` (without the `v` prefix).

Requires a dedicated Fluxer OAuth application and an Admin API key. The key and its owner need `user:list:dm_channels` and `message:lookup`; the owner also needs `admin:authenticate`.

### Install on your server with Docker Compose

Prerequisites: Git, Docker Engine with the Compose plugin (`docker compose`), and a public hostname with an HTTPS reverse proxy on the server. The Docker build includes the .NET SDK; you do not need to install .NET on the host.

1. Clone the repository and create the environment file:

   ```bash
   git clone https://github.com/JJConsulting/JJConsulting.Fluxer.git
   cd JJConsulting.Fluxer
   cp .env.example .env
   chmod 600 .env
   ```

2. Edit `.env` with your instance and credentials:

   ```dotenv
   ASPNETCORE_ENVIRONMENT=Production
   AllowedHosts=localhost;127.0.0.1;[::1];mcp.example.com
   Fluxer__ApiBaseUrl=https://your-fluxer-instance.com/api
   Fluxer__ClientId=YOUR_CLIENT_ID
   Fluxer__ClientSecret=YOUR_CLIENT_SECRET
   Fluxer__AdminApiKey=YOUR_ADMIN_API_KEY
   Fluxer__PublicUrl=https://mcp.example.com
   Fluxer__ClientRedirectUri=https://chatgpt.com/connector_platform_oauth_redirect
   ```

   Replace `mcp.example.com` with your public hostname. `Fluxer__ApiBaseUrl` must use HTTPS and omit `/v1` and the trailing slash. `Fluxer__PublicUrl` must be the public HTTPS origin, without a path or trailing slash. Use the exact ChatGPT callback for `Fluxer__ClientRedirectUri` (see **Connect ChatGPT** below). Keep `.env` private and out of Git.

3. In your Fluxer OAuth application, register `https://mcp.example.com/auth/fluxer/callback` as a redirect URI.

4. To use the published image, replace the contents of `compose.yaml` with the example below. Replace `<version>` with a published release version, without the `v` prefix:

   ```yaml
   services:
     fluxer-mcp:
       image: ghcr.io/jjconsulting/fluxer-mcp:<version>
       restart: unless-stopped
       ports:
         - "127.0.0.1:8080:8080"
       env_file: .env
   ```

   Start the MCP server:

   ```bash
   docker compose pull
   docker compose up -d
   docker compose ps
   docker compose logs --tail=100 fluxer-mcp
   curl --fail http://127.0.0.1:8080/health
   ```

   To build from source instead, keep the repository's original `compose.yaml` and run `docker compose up -d --build` instead of the pull and start commands above.

   Both configurations publish HTTP port `8080` only on the host's loopback interface and restart the container unless explicitly stopped. Run one instance because pending OAuth logins are stored in memory.

5. Configure **Nginx or Caddy** below, then verify the public endpoints.

   Both examples assume the proxy runs on the host. Point `mcp.example.com` to your server and allow inbound ports `80` and `443`.

   #### Nginx

   Add this to your existing HTTPS server block for `mcp.example.com`, with its TLS certificate configured:

   ```nginx
   location / {
       proxy_pass http://127.0.0.1:8080;
       proxy_http_version 1.1;
       proxy_set_header Host $host;
       proxy_set_header Connection "";
       proxy_buffering off;
       proxy_read_timeout 3600s;
   }
   ```

   ```bash
   sudo nginx -t && sudo systemctl reload nginx
   ```

   #### Caddy

   Add to `/etc/caddy/Caddyfile`:

   ```caddyfile
   mcp.example.com {
       reverse_proxy 127.0.0.1:8080 {
           flush_interval -1
       }
   }
   ```

   Caddy manages HTTPS certificates automatically. See the [Caddy reverse proxy documentation](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).

   ```bash
   sudo caddy validate --config /etc/caddy/Caddyfile
   sudo systemctl reload caddy
   ```

   If the proxy runs in Docker, use a shared network and upstream `fluxer-mcp:8080` instead of `127.0.0.1:8080`.

6. Verify the public endpoints, then follow **Connect ChatGPT** below using `https://mcp.example.com/mcp`:

   ```bash
   curl --fail https://mcp.example.com/health
   curl --fail https://mcp.example.com/.well-known/oauth-authorization-server
   ```

   Both endpoints should return HTTP `200`, and the OAuth metadata's `issuer` should equal `Fluxer__PublicUrl`.

After editing `.env`, run `docker compose up -d` to apply changes. To update the published image, change its version in `compose.yaml`, then run `docker compose pull` followed by `docker compose up -d`. For source builds, run `git pull` followed by `docker compose up -d --build`. To stop it, run `docker compose down`. Restarts discard pending OAuth logins; retry any sign-in that was in progress.

### Run locally with .NET

Requires **.NET SDK 11.0.100**.

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

### Connect ChatGPT

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

### MCP Tools

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
