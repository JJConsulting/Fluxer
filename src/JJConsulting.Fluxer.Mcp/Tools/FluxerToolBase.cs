using System.Security.Claims;
using JJConsulting.Fluxer.Mcp.Authentication;
using ModelContextProtocol;

namespace JJConsulting.Fluxer.Mcp.Tools;

public abstract class FluxerToolBase(IHttpContextAccessor accessor)
{
    protected string UserId()
    {
        var user = accessor.HttpContext?.User;
        var id = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user?.Identity?.IsAuthenticated != true || !user.HasClaim("scope", FluxerOptions.Scope) ||
            !FluxerOptions.IsId(id))
            throw new McpException("Authentication with identify scope is required.");
        return id!;
    }

    protected static void ValidatePage(int limit, int maximum, string? cursor, bool allowZero = true)
    {
        if (limit < 1 || limit > maximum ||
            cursor is not null && !(allowZero && cursor == "0") && !FluxerOptions.IsId(cursor))
            throw new McpException("Invalid pagination.");
    }

    protected static McpException Unavailable() => new("Fluxer data could not be verified. Try again later.");
}
