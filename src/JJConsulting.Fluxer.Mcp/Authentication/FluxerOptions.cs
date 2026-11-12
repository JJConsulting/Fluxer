namespace JJConsulting.Fluxer.Mcp.Authentication;

public sealed class FluxerOptions
{
    public const string Scheme = "Fluxer";
    public const string Scope = "identify";
    public string? PublicUrl { get; set; }
    public string ApiBaseUrl { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientRedirectUri { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string GetPublicUrl(HttpRequest request) =>
        string.IsNullOrEmpty(PublicUrl) ? $"{request.Scheme}://{request.Host}" : PublicUrl;

    internal static bool IsId(string? value) => value is { Length: > 0 and <= 19 } &&
                                                value[0] != '0' && value.All(char.IsAsciiDigit) &&
                                                long.TryParse(value, out _);
}