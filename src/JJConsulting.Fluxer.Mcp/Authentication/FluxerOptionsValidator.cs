using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Mcp.Authentication;

internal sealed class FluxerOptionsValidator : IValidateOptions<FluxerOptions>
{
    public ValidateOptionsResult Validate(string? name, FluxerOptions options)
    {
        var errors = new List<string>();

        if (!string.IsNullOrEmpty(options.PublicUrl) &&
            (!Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
             origin.Host.Length == 0 || origin.UserInfo != "" || origin.GetLeftPart(UriPartial.Authority) != options.PublicUrl))
            errors.Add("Fluxer:PublicUrl must be an HTTPS origin without credentials, path, query, fragment, or trailing slash.");

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var api) || api.Scheme != "https" ||
            api.Host.Length == 0 || api.UserInfo != "" || api.Query != "" || api.Fragment != "" || options.ApiBaseUrl.EndsWith('/'))
            errors.Add("Fluxer:ApiBaseUrl must be an absolute HTTPS URL without credentials, query, fragment, or trailing slash.");

        if (!Uri.TryCreate(options.ClientRedirectUri, UriKind.Absolute, out var redirect) || redirect.Scheme != "https" ||
            redirect.Host.Length == 0 || redirect.UserInfo != "" || redirect.Fragment != "" ||
            options.ClientRedirectUri.Length > 2048)
            errors.Add("Fluxer:ClientRedirectUri must be an absolute HTTPS URL without credentials or fragment, with at most 2048 characters.");

        if (!FluxerOptions.IsId(options.ClientId))
            errors.Add("Fluxer:ClientId must be a positive Int64 identifier without leading zeros.");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            errors.Add("Fluxer:ClientSecret is required.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
