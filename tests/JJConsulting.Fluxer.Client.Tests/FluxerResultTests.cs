using System.Net;
using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Client.Tests;

public class FluxerResultTests
{
    [Fact]
    public void Result_Conversions_Preserve_Cases_And_Metadata()
    {
        var page = new FluxerDmPage { Channels = [] };
        FluxerResult<FluxerDmPage> success = page;
        FluxerError error = new FluxerRateLimitedError(TimeSpan.FromSeconds(2));
        FluxerResult<FluxerDmPage> failure = error;

        Assert.Same(page, success.Value);
        Assert.Equal(error, Assert.IsType<FluxerError>(failure.Value));
        Assert.IsType<FluxerRateLimitedError>(error.Value);
        Assert.Equal(FluxerFailure.RateLimited, error.Failure);
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(2), error.RetryAfter);
        Assert.Equal("success", Describe(success));
        Assert.Equal("failure", Describe(failure));
    }

    [Fact]
    public void Default_And_Explicit_Null_Results_Match_The_Null_Arm()
    {
        FluxerResult<FluxerDmPage> empty = default;
        FluxerResult<FluxerDmPage> nullPage = (FluxerDmPage)null!;

        Assert.Null(empty.Value);
        Assert.Equal("empty", Describe(empty));
        Assert.Equal("empty", Describe(nullPage));
    }

    [Fact]
    public void Error_Cases_Preserve_Their_Own_Metadata()
    {
        FluxerError local = new FluxerLocalError(FluxerFailure.Timeout);
        FluxerError http = new FluxerHttpError(FluxerFailure.UpstreamUnavailable,
            HttpStatusCode.ServiceUnavailable, TimeSpan.FromSeconds(60));

        Assert.IsType<FluxerLocalError>(local.Value);
        Assert.Equal(FluxerFailure.Timeout, local.Failure);
        Assert.Null(local.StatusCode);
        Assert.Null(local.RetryAfter);
        Assert.IsType<FluxerHttpError>(http.Value);
        Assert.Equal(FluxerFailure.UpstreamUnavailable, http.Failure);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, http.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(60), http.RetryAfter);
    }

    [Fact]
    public void Empty_Error_Is_Not_A_Valid_Failure()
    {
        FluxerError error = default;
        Assert.Null(error.Value);
        Assert.Throws<InvalidOperationException>(() => error.Failure);
    }

    private static string Describe(FluxerResult<FluxerDmPage> result)
    {
        return result switch
        {
            FluxerDmPage => "success",
            FluxerError => "failure",
            null => "empty"
        };
    }
}
