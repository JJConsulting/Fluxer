namespace JJConsulting.Fluxer.Client;

public sealed class FluxerClientOptions
{
    // Operator-approved API base, including any path prefix, without /v1.
    public string ApiBaseUrl { get; init; } = "";
    public string AdminApiKey { get; init; } = "";
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public int MaxResponseBytes { get; init; } = 1_048_576;
    public int MaxRetries { get; init; } = 2;
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public int DmPageSize { get; init; } = 200;
    public int MaxDmPages { get; init; } = 100;

    internal bool IsValid()
    {
        return Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) &&
               string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
               !string.IsNullOrEmpty(uri.Host) &&
               IsCredential(AdminApiKey) &&
               RequestTimeout >= TimeSpan.FromMilliseconds(1) && RequestTimeout <= TimeSpan.FromMinutes(2) &&
               MaxResponseBytes is >= 1 and <= 16_777_216 && MaxRetries is >= 0 and <= 3 &&
               MaxRetryDelay >= TimeSpan.Zero && MaxRetryDelay <= TimeSpan.FromSeconds(30) &&
               DmPageSize is >= 1 and <= 200 && MaxDmPages is >= 1 and <= 1000;
    }

    internal static bool IsCredential(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.All(c => c is >= '!' and <= '~');
    }
}
