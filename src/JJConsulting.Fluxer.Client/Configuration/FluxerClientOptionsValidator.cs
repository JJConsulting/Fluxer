using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client.Configuration;

internal sealed class FluxerClientOptionsValidator : IValidateOptions<FluxerClientOptions>
{
    public ValidateOptionsResult Validate(string? name, FluxerClientOptions options)
    {
        var errors = new List<string>();
        
        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || string.IsNullOrEmpty(uri.Host))
            errors.Add("Fluxer:ApiBaseUrl must be an absolute HTTPS URL without credentials, query, or fragment.");

        if (!FluxerClientOptions.IsCredential(options.AdminApiKey))
            errors.Add("Fluxer:AdminApiKey must contain only visible ASCII characters and cannot be empty.");

        if (options.RequestTimeout < TimeSpan.FromMilliseconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(2))
            errors.Add("Fluxer:RequestTimeout must be between 1 millisecond and 2 minutes.");

        if (options.MaxResponseBytes is < 1 or > 16_777_216)
            errors.Add("Fluxer:MaxResponseBytes must be between 1 and 16777216.");

        if (options.MaxRetries is < 0 or > 3)
            errors.Add("Fluxer:MaxRetries must be between 0 and 3.");

        if (options.MaxRetryDelay < TimeSpan.Zero || options.MaxRetryDelay > TimeSpan.FromSeconds(30))
            errors.Add("Fluxer:MaxRetryDelay must be between zero and 30 seconds.");

        if (options.DmPageSize is < 1 or > 200)
            errors.Add("Fluxer:DmPageSize must be between 1 and 200.");

        if (options.MaxDmPages is < 1 or > 1000)
            errors.Add("Fluxer:MaxDmPages must be between 1 and 1000.");

        if (errors.Count == 0)
            return ValidateOptionsResult.Success;
        
        return ValidateOptionsResult.Fail(errors);
    }
}
