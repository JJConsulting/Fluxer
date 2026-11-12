namespace JJConsulting.Fluxer.Mcp.Authentication;

internal sealed record OAuthError(string Error, int StatusCode = 400);
internal sealed record Revoked;
    
internal union OAuthResult(TokenResponse, Revoked, OAuthError);

internal union OAuthFormResult(IFormCollection, OAuthError);