# Multi-stage build for the ASP.NET Core API. Cloudflare builds this image on
# `wrangler deploy` (see cloudflare/wrangler.jsonc) and runs it as a Container
# behind the Worker; docker-compose.yml uses the same image locally.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY pokemon-tcg-api.csproj ./
RUN dotnet restore pokemon-tcg-api.csproj
COPY . ./
RUN dotnet publish pokemon-tcg-api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
COPY --from=build /app ./
# The aspnet image ships a non-root "app" user; never run the API as root.
USER app
ENTRYPOINT ["dotnet", "pokemon-tcg-api.dll"]
