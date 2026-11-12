using System.Net;

namespace JJConsulting.Fluxer.Client.Models;

public sealed record FluxerLocalError(FluxerFailure Failure);

public sealed record FluxerHttpError(FluxerFailure Failure, HttpStatusCode StatusCode, TimeSpan? RetryAfter = null);

public sealed record FluxerRateLimitedError(TimeSpan? RetryAfter = null);

public union FluxerError(FluxerLocalError, FluxerHttpError, FluxerRateLimitedError)
{
    public FluxerFailure Failure => this switch
    {
        FluxerLocalError error => error.Failure,
        FluxerHttpError error => error.Failure,
        FluxerRateLimitedError => FluxerFailure.RateLimited,
        _ => throw new InvalidOperationException("Empty Fluxer error.")
    };

    public HttpStatusCode? StatusCode => this switch
    {
        FluxerHttpError error => error.StatusCode,
        FluxerRateLimitedError => HttpStatusCode.TooManyRequests,
        _ => null
    };

    public TimeSpan? RetryAfter => this switch
    {
        FluxerHttpError error => error.RetryAfter,
        FluxerRateLimitedError error => error.RetryAfter,
        _ => null
    };
}
