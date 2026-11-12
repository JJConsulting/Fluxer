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

    public void Validate()
    {
        if ((!string.IsNullOrEmpty(PublicUrl) &&
             (!Uri.TryCreate(PublicUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
              origin.Host.Length == 0 || origin.UserInfo != "" || origin.GetLeftPart(UriPartial.Authority) != PublicUrl)) ||
            !Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var api) || api.Scheme != "https" ||
            api.Host.Length == 0 || api.UserInfo != "" || api.Query != "" || api.Fragment != "" || ApiBaseUrl.EndsWith('/') ||
            !Uri.TryCreate(ClientRedirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != "https" ||
            redirect.Host.Length == 0 || redirect.UserInfo != "" || redirect.Fragment != "" ||
            ClientRedirectUri.Length > 2048 || !IsId(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("Fluxer OAuth settings are missing or invalid.");
    }

    internal static bool IsId(string? value) => value is { Length: > 0 and <= 19 } &&
                                                value[0] != '0' && value.All(char.IsAsciiDigit) &&
                                                long.TryParse(value, out _);
}