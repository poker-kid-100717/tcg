FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY pokemon-tcg-api.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# All state lives in Postgres, so the container needs no writable storage
# and runs as the image's built-in non-root user.
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "pokemon-tcg-api.dll"]
