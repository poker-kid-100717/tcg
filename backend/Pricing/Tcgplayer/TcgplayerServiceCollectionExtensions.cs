namespace PokemonTCG.API.Pricing.Tcgplayer;

/// <summary>Optional one-call registration for the TCGplayer client; Program.cs may wire it differently.</summary>
public static class TcgplayerServiceCollectionExtensions
{
    /// <summary>
    /// Binds <see cref="TcgplayerOptions"/>, registers the singleton token cache,
    /// the auth handler and the typed <see cref="TcgplayerClient"/>. Returns the
    /// client's builder so callers can add resilience handlers.
    /// Expects a <see cref="TimeProvider"/> to be registered.
    /// </summary>
    public static IHttpClientBuilder AddTcgplayerClient(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(TcgplayerOptions.SectionName);
        services.Configure<TcgplayerOptions>(section);
        var options = section.Get<TcgplayerOptions>() ?? new TcgplayerOptions();

        services.AddHttpClient(TcgplayerTokenProvider.HttpClientName);
        services.AddSingleton<TcgplayerTokenProvider>();
        services.AddTransient<TcgplayerAuthHandler>();
        return services.AddHttpClient<TcgplayerClient>(client =>
            {
                client.BaseAddress = options.VersionedBaseUri;
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .AddHttpMessageHandler<TcgplayerAuthHandler>();
    }
}
