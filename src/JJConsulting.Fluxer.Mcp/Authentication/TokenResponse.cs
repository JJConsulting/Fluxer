using System.Text.Json.Serialization;

namespace JJConsulting.Fluxer.Mcp.Authentication;

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; } 

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; } 

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; init; } 

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
    
}