using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json.Serialization;
using JJConsulting.Fluxer.Mcp.Authentication;
using ModelContextProtocol.Server;

namespace JJConsulting.Fluxer.Mcp.Tools;

public sealed record FluxerProfile([property: JsonPropertyName("id")] string Id);

[McpServerToolType]
public sealed class IdentityTools
{
    [McpServerTool(Name = "get_profile", ReadOnly = true, OpenWorld = false, UseStructuredContent = true)]
    [McpMeta("openai/profile", true)]
    [McpMeta("securitySchemes", JsonValue = """[{"type":"oauth2","scopes":["identify"]}]""")]
    [Description("Return the Fluxer account ID of the authenticated user.")]
    public static FluxerProfile GetProfile(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        var id = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        
        if (user?.Identity?.IsAuthenticated != true || !FluxerOptions.IsId(id))
            throw new InvalidOperationException("Authentication required.");
        
        return new FluxerProfile(id!);
    }
}
