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

    internal static bool IsCredential(string? value)
    {
        return !string.IsNullOrEmpty(value) && value.All(c => c is >= '!' and <= '~');
    }
}
