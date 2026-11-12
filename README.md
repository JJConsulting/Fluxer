# Fluxer MCP

HTTP MCP server: users sign in through Fluxer OAuth, and the server queries the Admin API with its own key, restricting data to the authenticated user.

## Run locally

Requirements: **.NET SDK 11.0.100**, a Fluxer OAuth application dedicated to this MCP, and an Admin API key with permission to read DMs and messages.

From the repository root:

```bash
dotnet dev-certs https --trust
```

Edit `src/JJConsulting.Fluxer.Mcp/appsettings.json`:

```json
{
  "AllowedHosts": "localhost;127.0.0.1;[::1]",
  "Fluxer": {
    "ApiBaseUrl": "https://your-fluxer-instance.com/api",
    "ClientId": "1234567890123456789",
    "ClientSecret": "YOUR_CLIENT_SECRET",
    "ClientRedirectUri": "https://chatgpt.com/connector_platform_oauth_redirect",
    "AdminApiKey": "YOUR_ADMIN_API_KEY"
  }
}
```

- `ApiBaseUrl`: your Fluxer instance's API base URL, including its path prefix, without `/v1` or a trailing slash.
- `ClientId` and `ClientSecret`: credentials for the OAuth application dedicated to this MCP. Keep the secret on the server.
- `ClientRedirectUri`: the exact callback URL shown by your MCP client. The bridge only redirects to this address.
- `AdminApiKey`: the Admin API key used only by the server.

Git ignores `appsettings.Development.json`. Do not put real credentials in the example file.

Start the server:

```bash
dotnet run --project src/JJConsulting.Fluxer.Mcp --launch-profile https
```

Check that it is running:

```bash
curl https://localhost:7042/health
```

Expect HTTP `200`. This endpoint only checks that the server is running; credentials are used during authenticated requests.

## Connect an MCP client

1. In Fluxer, register **`https://localhost:7042/auth/fluxer/callback`** in the OAuth application. For a public deployment or ngrok tunnel, replace the origin with the public HTTPS URL.
2. In your MCP client, configure the MCP URL (`https://localhost:7042/mcp` locally), select OAuth with a predefined client, enter the same `ClientId`, and leave **Client Secret empty**. The token endpoint uses authentication method `none` with S256 PKCE. Request scope `identify`.
3. Copy the callback URL shown by your MCP client into `Fluxer:ClientRedirectUri` on the server, then restart it. This is the client's callback, not `/auth/fluxer/callback`.
4. Refresh the MCP connection so it discovers `/oauth/authorize` and `/oauth/token` on this server. Sign in and authorize the application in Fluxer. If you recreate the connection, update `ClientRedirectUri` if its callback URL changes.

For ChatGPT, this server advertises RFC 9207 issuer identification and returns `iss` on successful and error redirects, enabling the stable callback `https://chatgpt.com/connector_platform_oauth_redirect`. Set `Fluxer:PublicUrl` to the fixed public HTTPS origin so discovery and callback responses use the same issuer. After deploying, create or reconfigure the ChatGPT connection and confirm its displayed callback matches `ClientRedirectUri`; an existing connection may still use its previous callback. The callback registered in Fluxer remains your server's `/auth/fluxer/callback`. See [OpenAI authentication documentation](https://developers.openai.com/plugins/build/auth#protect-callbacks-with-issuer-identification).

The MCP bridges authorization, token exchange, refresh and revocation to Fluxer, adding the client secret only in server-to-server requests. Access and refresh tokens are issued by Fluxer. The MCP validates access tokens through introspection and uses the authenticated user ID to filter Admin API queries. Both the client secret and Admin API key stay on the server.

Pending logins expire after 10 minutes; exchange codes expire after 1 minute and can be used once. Pending logins are kept in memory, so restart the login after a server restart. Run one MCP process; multiple replicas require shared login state.

## Set up ChatGPT

1. Start the MCP server and expose it through a public HTTPS URL. For local development, keep the server running and start `ngrok http https://localhost:7042` in another terminal. ChatGPT cannot reach your machine through a `localhost` URL.
2. Add the public hostname to `AllowedHosts` and set `Fluxer:PublicUrl` to the public origin, such as `https://mcp.example.com`, without `/mcp` or a trailing slash. In the Fluxer OAuth application, register `https://mcp.example.com/auth/fluxer/callback`. Restart the server after changing its settings.
3. Open ChatGPT's app settings and create a custom app/MCP connection. Enable developer mode if your account requires it; workspace permissions may also control access. Use the following settings where the corresponding fields are available:

   | Field | Value |
   | --- | --- |
   | Name | `Fluxer MCP` |
   | MCP server URL | `https://mcp.example.com/mcp` (replace with your public URL) |
   | Authentication | `OAuth` |
   | Registration / Client mode | `Pre-defined` |
   | Client ID | The same value as `Fluxer:ClientId` |
   | Client Secret | Leave empty; the Fluxer secret stays on the MCP server |
   | Token endpoint authentication | `None` |
   | Registration URL | Leave empty |
   | Scope | `identify` |

   This server uses a predefined OAuth client and PKCE. It does not implement Dynamic Client Registration (DCR). If Registration URL is required, check that you selected **Pre-defined**, not DCR. Do not enter the authorization endpoint, metadata URL, or ChatGPT callback in that field.

4. Confirm the callback URL shown by ChatGPT exactly matches `Fluxer:ClientRedirectUri`. The server supports the stable callback `https://chatgpt.com/connector_platform_oauth_redirect`, but an existing connection may still show `https://chatgpt.com/connector/oauth/{callback_id}`. Use the exact displayed value and restart the server if you change it. This ChatGPT callback belongs in the MCP configuration; the callback registered in Fluxer remains `https://mcp.example.com/auth/fluxer/callback`.
5. Connect the app, sign in to Fluxer, and authorize access. In a new ChatGPT conversation, select the app and ask it to retrieve your Fluxer profile or recent conversations. Refresh the app's tool list after server updates that add or change tools.

Before retrying a failed connection, check the public `/health` and `/.well-known/oauth-authorization-server` URLs. Both should return HTTP `200`; OAuth metadata should include `authorization_response_iss_parameter_supported: true` and an `issuer` matching `Fluxer:PublicUrl`. An ngrok `502` with `connection refused` means the local server is unavailable at the tunnel's configured address. Keep both the server and tunnel running while connecting and using the app. If the tunnel hostname changes, update `AllowedHosts`, `PublicUrl`, the Fluxer callback, and the ChatGPT MCP server URL.

See the official OpenAI guides for [connecting a custom MCP server](https://developers.openai.com/api/docs/guides/custom-mcp-server) and [OAuth authentication and client registration](https://developers.openai.com/plugins/build/auth).

## Tools

| Tool | Purpose |
| --- | --- |
| `get_profile` | Get the authenticated Fluxer user ID. |
| `list_my_dm_channels` | Page through your individual DMs with participant names and last activity. Channel cursors follow creation IDs. |
| `get_my_recent_conversations` | List your most recently active DMs with names and the latest message preview. |
| `find_my_dm_contacts` | Find a DM by the other person's display name, username or ID. |
| `get_my_dm_messages` | Read a verified DM with author names; optionally filter by start/end timestamps. |
| `get_my_dm_activity` | Collect messages across your DMs for a day or period, with names and explicit continuation cursors. |
| `search_my_dm_messages` | Search text inside one verified DM, returning author names and the match count. |

For date filters, pass ISO-8601 timestamps with explicit UTC offsets. For a day in São Paulo, for example, use `start: "2026-11-11T00:00:00-03:00"` and `end: "2026-11-12T00:00:00-03:00"`. Start is inclusive; end is exclusive. Activity returns up to 100 messages per channel: continue `NextChannelBefore` for more channels and use `get_my_dm_messages` with the same start/end and each conversation's `NextBefore` while `HasMore` is true. Summaries should disclose incomplete coverage.

Text search uses Fluxer's message search index, returns at most 100 matches and reports `Truncated` when more matches exist. An empty result can also mean the instance has no message search index configured. The MCP does not infer promises, deadlines or task status; ChatGPT draws conclusions from the returned conversation evidence.

Names come from the existing DM/message responses, without exposing emails or account administration fields. All tools remain limited to the signed-in user's verified, non-NSFW individual DMs. The key and its owner need `user:list:dm_channels` for listing/membership and `message:lookup` for reading/searching messages; the owner also needs `admin:authenticate`. After adding tools, restart the server and refresh the MCP connection's tool list in ChatGPT.

For ngrok with the HTTPS launch profile, use the HTTPS upstream explicitly:

```bash
ngrok http https://localhost:7042
```

Using `ngrok http 7042` targets HTTP and fails against this HTTPS listener. Add the ngrok hostname to `AllowedHosts`, set `Fluxer:PublicUrl` to its public HTTPS origin, and register that origin plus `/auth/fluxer/callback` in Fluxer.

Remote clients need a reachable public HTTPS URL; `localhost` works for clients running on your machine. List the request host in `AllowedHosts`. The server derives its public URL from each HTTPS request.

Optional: set `Fluxer:PublicUrl` (for example, `https://mcp.example.com`, without a path or trailing slash) to advertise a fixed public address behind a reverse proxy. If omitted, the proxy must forward the original HTTPS scheme and host through ASP.NET Core's trusted forwarded headers configuration.
