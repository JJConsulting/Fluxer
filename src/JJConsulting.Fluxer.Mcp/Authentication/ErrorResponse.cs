using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace JJConsulting.Fluxer.Mcp.Authentication;

internal sealed class ErrorResponse
{
    public ErrorResponse()
    {
        
    }

    [SetsRequiredMembers]
    public ErrorResponse(string error)
    {
        Error = error;
    }
    
    [JsonPropertyName("error")]
    public required string? Error { get; init; }
}