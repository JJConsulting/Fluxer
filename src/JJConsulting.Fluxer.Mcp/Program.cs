using JJConsulting.Fluxer.Mcp.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.AddFluxerLogging();
builder.Services.AddFluxerMcp(builder.Configuration);

var app = builder.Build();

app.UseFluxerMcp();

app.Run();
