using System.Net;
using JJConsulting.Fluxer.Client.Models;

namespace JJConsulting.Fluxer.Client.Tests;

public class FluxerResultTests
{
    [Fact]
    public void Result_Conversions_Preserve_Reference_Cases_And_Metadata()
    {
        var page = new FluxerDmPage { Channels = [] };
        FluxerResult<FluxerDmPage> success = page;
        var error = new FluxerError(FluxerFailure.RateLimited, HttpStatusCode.TooManyRequests,
            TimeSpan.FromSeconds(2));
        FluxerResult<FluxerDmPage> failure = error;

        Assert.Same(page, success.Value);
        Assert.Same(error, failure.Value);
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
        FluxerResult<FluxerDmPage> nullError = (FluxerError)null!;

        Assert.Null(empty.Value);
        Assert.Equal("empty", Describe(empty));
        Assert.Equal("empty", Describe(nullPage));
        Assert.Equal("empty", Describe(nullError));
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
