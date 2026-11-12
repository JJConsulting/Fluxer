using System.Net;

namespace JJConsulting.Fluxer.Client.Models;

public sealed class FluxerError(FluxerFailure failure, HttpStatusCode? statusCode = null,
    TimeSpan? retryAfter = null)
{
    public FluxerFailure Failure { get; } = failure;
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
