namespace JJConsulting.Fluxer.Mcp.Configuration;

internal static class LoggingBuilderExtensions
{
    public static ILoggingBuilder AddFluxerLogging(this ILoggingBuilder logging)
    {
        // Request diagnostics can include OAuth credentials and private message text.
        logging.AddFilter((category, level) =>
            category == "JJConsulting.Fluxer.Mcp" && level >= LogLevel.Information);

        return logging;
    }
}
