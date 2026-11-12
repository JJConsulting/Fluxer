using System.Text.Json.Serialization;

namespace JJConsulting.Fluxer.Mcp.Authentication;

internal sealed class FluxerTokenResponse
{
    [JsonPropertyName("active")] 
    public bool Active { get; init; }

    [JsonPropertyName("client_id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; init; } 

    [JsonPropertyName("sub")] 
    public string? Subject { get; init; }
    [JsonPropertyName("exp")]
    public long ExpiresAt { get; init; } 
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}