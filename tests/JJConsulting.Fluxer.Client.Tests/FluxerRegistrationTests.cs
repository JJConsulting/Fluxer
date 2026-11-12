using System.Net;
using System.Text;
using JJConsulting.Fluxer.Client;
using JJConsulting.Fluxer.Client.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JJConsulting.Fluxer.Client.Tests;

public class FluxerRegistrationTests
{
    private static IConfigurationSection Configuration(string? url = "https://fluxer.example/api", string? key = "sentinel-key",
        string setting = "MaxRetries", string value = "2")
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FluxerClient:ApiBaseUrl"] = url,
                ["FluxerClient:AdminApiKey"] = key,
                [$"FluxerClient:{setting}"] = value
            }).Build().GetSection("FluxerClient");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://fluxer.example/api")]
    [InlineData("https://user:secret@fluxer.example/api")]
    [InlineData("https://fluxer.example/api?dest=other")]
    [InlineData("https://fluxer.example/api#other")]
    [InlineData("/api")]
    public async Task Startup_Validation_Rejects_Unsafe_Or_Missing_Api_Base_Without_Echoing_Secrets(string? url)
    {
        var services = new ServiceCollection().AddFluxerClient(Configuration(url));
        await using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<OptionsValidationException>(async () => await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));
        Assert.DoesNotContain("sentinel-key", error.ToString());
        Assert.DoesNotContain("user:secret", error.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("key\r\nInjected: value")]
    public async Task Startup_Validation_Rejects_Missing_Or_Invalid_Key(string? key)
    {
        var services = new ServiceCollection().AddFluxerClient(Configuration(key: key));
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<OptionsValidationException>(async () => await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("MaxRetries", "4")]
    [InlineData("MaxRetries", "-1")]
    [InlineData("MaxResponseBytes", "0")]
    [InlineData("MaxResponseBytes", "16777217")]
    [InlineData("MaxDmPages", "0")]
    [InlineData("DmPageSize", "201")]
    [InlineData("RequestTimeout", "00:00:00")]
    [InlineData("MaxRetryDelay", "00:00:31")]
    public async Task Startup_Validation_Rejects_Unbounded_Limits(string setting, string value)
    {
        var services = new ServiceCollection().AddFluxerClient(Configuration(setting: setting, value: value));
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<OptionsValidationException>(async () => await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Both_Registered_Clients_Disable_Redirects_And_Cookies_And_Use_Distinct_Handlers()
    {
        var services = new ServiceCollection().AddFluxerClient(Configuration());
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(CancellationToken.None);
        Assert.IsType<FluxerAdminClient>(provider.GetRequiredService<FluxerAdminClient>());
        Assert.IsType<FluxerIdentityClient>(provider.GetRequiredService<FluxerIdentityClient>());
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var admin = Primary(factory.CreateHandler(nameof(FluxerAdminClient)));
        var identity = Primary(factory.CreateHandler(nameof(FluxerIdentityClient)));
        Assert.NotSame(admin, identity);
        Assert.False(admin.AllowAutoRedirect);
        Assert.False(identity.AllowAutoRedirect);
        Assert.False(admin.UseCookies);
        Assert.False(identity.UseCookies);
    }

    [Fact]
    public async Task Factory_Clients_Keep_Admin_And_Identity_Credentials_Separate_And_Emit_No_Request_Logs()
    {
        var entries = new List<string>();
        var requests = new List<(Uri? Url, string? Authorization)>();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(new CaptureLoggerProvider(entries)));
        services.AddFluxerClient(Configuration());
        services.AddHttpClient<FluxerAdminClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(requests, "{\"channels\":[]}"));
        services.AddHttpClient<FluxerIdentityClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new CaptureHandler(requests,
                "{\"id\":\"123\",\"sub\":\"123\",\"username\":\"sentinel-message\"}"));
        await using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<FluxerAdminClient>().GetIndividualDmPageAsync("123");
        await provider.GetRequiredService<FluxerIdentityClient>().GetIdentityAsync("sentinel-oauth");
        Assert.Equal(new[] { "Admin sentinel-key", "Bearer sentinel-oauth" }, requests.Select(r => r.Authorization));
        Assert.All(requests, r => Assert.Equal("fluxer.example", r.Url?.Host));
        Assert.DoesNotContain(entries, e => e.Contains("sentinel-", StringComparison.Ordinal));
        Assert.DoesNotContain(entries, e => e.Contains("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, e => e.Contains("oauth2/userinfo", StringComparison.Ordinal));
    }

    private static SocketsHttpHandler Primary(HttpMessageHandler handler)
    {
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;
        return Assert.IsType<SocketsHttpHandler>(handler);
    }

    private sealed class CaptureHandler(List<(Uri? Url, string? Authorization)> requests, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add((request.RequestUri, request.Headers.Authorization?.ToString()));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class CaptureLoggerProvider(List<string> entries) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new CaptureLogger(entries);
        }

        public void Dispose() { }
    }

    private sealed class CaptureLogger(List<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Add(formatter(state, exception));
        }
    }
}
