namespace JJConsulting.Fluxer.Client.Models;

public enum FluxerFailure
{
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    UpstreamUnavailable,
    UnexpectedStatus,
    RedirectRejected,
    InvalidResponse,
    ResponseTooLarge,
    Timeout,
    PaginationLimitExceeded
}
