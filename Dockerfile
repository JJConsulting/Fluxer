FROM mcr.microsoft.com/dotnet/sdk:11.0.100 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/JJConsulting.Fluxer.Client/JJConsulting.Fluxer.Client.csproj src/JJConsulting.Fluxer.Client/
COPY src/JJConsulting.Fluxer.Mcp/JJConsulting.Fluxer.Mcp.csproj src/JJConsulting.Fluxer.Mcp/
RUN dotnet restore src/JJConsulting.Fluxer.Mcp/JJConsulting.Fluxer.Mcp.csproj

COPY src/ src/
RUN dotnet publish src/JJConsulting.Fluxer.Mcp/JJConsulting.Fluxer.Mcp.csproj \
    -c Release --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS final
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "JJConsulting.Fluxer.Mcp.dll"]
