# ASP.NET Core API image, deployed to Cloudflare Containers (linux/amd64).
# Build context is this backend/ directory (see cloudflare/wrangler.jsonc).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between code-only changes.
COPY pokemon-tcg-api.csproj ./
RUN dotnet restore pokemon-tcg-api.csproj

COPY . ./
RUN dotnet publish pokemon-tcg-api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ASP.NET Core images listen on 8080 by default; the Worker's Container
# class forwards /api/* requests to this port.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

COPY --from=build /app/publish ./

# Run as the non-root user shipped with the .NET images.
USER $APP_UID

ENTRYPOINT ["dotnet", "pokemon-tcg-api.dll"]
