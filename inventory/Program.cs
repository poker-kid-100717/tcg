using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Http.Resilience;
using TcgSignal.Inventory;
using TcgSignal.Inventory.Application;
using TcgSignal.Inventory.Domain;
using TcgSignal.Inventory.Infrastructure;
using TcgSignal.Inventory.Providers.BestBuy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<InventoryExceptionHandler>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();

var rawConnection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(rawConnection))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

var options = builder.Configuration.GetSection(InventoryOptions.SectionName).Get<InventoryOptions>() ?? new();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new InventoryStore(PostgresConnectionString.Normalize(rawConnection)));
builder.Services.AddScoped<InventorySearchService>();

builder.Services.AddHttpClient<BestBuyInventoryProvider>(client =>
{
    client.BaseAddress = new Uri("https://api.bestbuy.com/");
    client.Timeout = TimeSpan.FromSeconds(20);
}).AddStandardResilienceHandler(resilience =>
{
    resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
    resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddScoped<IInventoryProvider>(sp => sp.GetRequiredService<BestBuyInventoryProvider>());

var app = builder.Build();
app.UseExceptionHandler();

await app.Services.GetRequiredService<InventoryStore>().InitializeAsync(CancellationToken.None);

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health", async (InventoryStore store, CancellationToken ct) =>
    await store.CanConnectAsync(ct)
        ? Results.Ok(new { status = "Healthy" })
        : Results.Problem(statusCode: 503, title: "Inventory database unavailable"));

app.MapPost("/api/inventory/nearby", async Task<IResult> (
    InventoryQuery query,
    InventorySearchService service,
    CancellationToken ct) =>
{
    if (query.Latitude is < -90 or > 90 || query.Longitude is < -180 or > 180)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["location"] = ["A valid latitude and longitude are required."] });

    var miles = Math.Clamp(query.RadiusMiles, 1, 100);
    return Results.Ok(await service.SearchAsync(query with { RadiusMiles = miles }, ct));
});

app.Run();

public partial class Program;

public sealed class InventoryExceptionHandler(ILogger<InventoryExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Inventory request failed");
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Inventory temporarily unavailable",
            detail: "A retailer or inventory dependency could not be verified. No unverified stock was shown.")
            .ExecuteAsync(context);
        return true;
    }
}
