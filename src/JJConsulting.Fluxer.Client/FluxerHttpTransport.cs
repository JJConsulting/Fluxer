using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using JJConsulting.Fluxer.Client.Models;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client;

internal sealed class FluxerHttpTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _client;
    private readonly FluxerClientOptions _options;
    private readonly Uri _apiBase;

    internal FluxerHttpTransport(HttpClient client, FluxerClientOptions options)
    {
        if (!options.IsValid())
            throw new OptionsValidationException(nameof(FluxerClientOptions), typeof(FluxerClientOptions),
                ["Fluxer client settings are missing or invalid."]);

        _client = client;
        _options = options;
        _apiBase = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
    }

    internal async Task<FluxerResult<T>> GetAsync<T>(string path, string scheme, string credential, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        if (!FluxerClientOptions.IsCredential(credential))
            throw new ArgumentException("Invalid Fluxer credential.", nameof(credential));

        var destination = new Uri(_apiBase, path);
        if (!_apiBase.IsBaseOf(destination))
            throw new InvalidOperationException("Fluxer destination is outside the configured API base.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_options.RequestTimeout);
        var token = deadline.Token;
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                TimeSpan delay;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, destination);
                    request.Headers.Authorization = new AuthenticationHeaderValue(scheme, credential);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    var status = response.StatusCode;
                    if ((int)status is >= 300 and <= 399)
                        return new FluxerError(FluxerFailure.RedirectRejected, status);

                    var bodyResult = await ReadBodyAsync(response.Content, token);
                    if (bodyResult is FluxerError bodyError) return bodyError;
                    if (bodyResult is not byte[] body)
                        return new FluxerError(FluxerFailure.InvalidResponse);
                    token.ThrowIfCancellationRequested();
                    if (status == HttpStatusCode.OK)
                    {
                        try
                        {
                            var result = JsonSerializer.Deserialize<T>(body, JsonOptions);
                            if (result is null)
                                return new FluxerError(FluxerFailure.InvalidResponse);
                            token.ThrowIfCancellationRequested();
                            return result;
                        }
                        catch (JsonException)
                        {
                            // JSON exceptions can include upstream content; return only a local failure.
                            return new FluxerError(FluxerFailure.InvalidResponse);
                        }
                    }

                    FluxerFailure failure = status switch
                    {
                        HttpStatusCode.Unauthorized => FluxerFailure.Unauthorized,
                        HttpStatusCode.Forbidden => FluxerFailure.Forbidden,
                        HttpStatusCode.NotFound => FluxerFailure.NotFound,
                        HttpStatusCode.TooManyRequests => FluxerFailure.RateLimited,
                        _ when IsTransient(status) => FluxerFailure.UpstreamUnavailable,
                        _ => FluxerFailure.UnexpectedStatus
                    };
                    var retryAfter = GetRetryAfter(response, body);
                    var error = new FluxerError(failure, status, retryAfter);
                    if (attempt >= _options.MaxRetries ||
                        !(IsTransient(status) || status == HttpStatusCode.TooManyRequests) ||
                        (status == HttpStatusCode.TooManyRequests && retryAfter is null))
                        return error;

                    delay = retryAfter ?? Backoff(attempt);
                    // Never shorten a server-mandated wait to fit the local retry budget.
                    if (delay > _options.MaxRetryDelay)
                        return error;
                }
                catch (HttpRequestException error)
                {
                    if (attempt >= _options.MaxRetries || error.HttpRequestError is not
                        (HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.ResponseEnded))
                        return new FluxerError(FluxerFailure.UpstreamUnavailable);
                    delay = Backoff(attempt);
                }
                catch (IOException)
                {
                    if (attempt >= _options.MaxRetries)
                        return new FluxerError(FluxerFailure.UpstreamUnavailable);
                    delay = Backoff(attempt);
                }

                await Task.Delay(delay, token);
            }
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new FluxerError(FluxerFailure.Timeout);
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        return TimeSpan.FromMilliseconds(Math.Min(100 * (1 << attempt), _options.MaxRetryDelay.TotalMilliseconds));
    }

    private static bool IsTransient(HttpStatusCode status)
    {
        return status is
            HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    }

    private async Task<FluxerResult<byte[]>> ReadBodyAsync(HttpContent content, CancellationToken token)
    {
        if (content.Headers.ContentLength > _options.MaxResponseBytes)
            return new FluxerError(FluxerFailure.ResponseTooLarge);

        await using var stream = await content.ReadAsStreamAsync(token);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (body.Length + read > _options.MaxResponseBytes)
                return new FluxerError(FluxerFailure.ResponseTooLarge);
            await body.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return body.ToArray();
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response, byte[] body)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? delay = header?.Delta;
        if (header?.Date is { } date)
            {
            var remaining = date - DateTimeOffset.UtcNow;
            delay = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("retry_after", out var value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds) &&
                double.IsFinite(seconds) && seconds >= 0)
            {
                var fromBody = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
                if (delay is null || fromBody > delay)
                    delay = fromBody;
            }
        }
        catch (JsonException)
        {
            // Status and header remain usable when an error response has no valid JSON body.
        }
        return delay;
    }
}
