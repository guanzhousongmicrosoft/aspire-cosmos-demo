using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Polly;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument();

//build cosmos client
builder.AddAzureCosmosClient(
    "cosmos",
    settings =>
    {
        settings.DisableTracing = false;
    },
    clientOptions =>
    {
        clientOptions.ApplicationName = "cosmos-aspire";
        clientOptions.SerializerOptions = new()
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.Default
        };
        clientOptions.CosmosClientTelemetryOptions = new()
        {
            DisableDistributedTracing = false
        };
        clientOptions.ConnectionMode = ConnectionMode.Gateway;
    });


builder.Services.AddSingleton<DatabaseBootstrapper>();
builder.Services.AddHealthChecks()
    .Add(new("cosmos", sp => sp.GetRequiredService<DatabaseBootstrapper>(), null, null));
builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseBootstrapper>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseOpenApi();
    app.UseSwaggerUi();
}

app.UseExceptionHandler();

// Create new iot device
app.MapPost("/iots", async (IoTDevice iot, CosmosClient cosmosClient) =>
    (await cosmosClient.GetAppDataContainer().CreateItemAsync(iot)).Resource
);

// Get all the iot devices
app.MapGet("/iots", (CosmosClient cosmosClient) =>
    cosmosClient.GetAppDataContainer()
                .GetItemLinqQueryable<IoTDevice>(
                    requestOptions: new()
                    {
                        MaxItemCount = 10
                    })
                .ToFeedIterator()
                .ToAsyncEnumerable()
);

app.MapPut("/iots/{id}", async (string id, IoTDevice iot, CosmosClient cosmosClient) =>
    (await cosmosClient.GetAppDataContainer().ReplaceItemAsync(iot, id)).Resource
);

app.MapDelete("/iots/{userId}/{id}", async (string userId, string id, CosmosClient cosmosClient) =>
{
    await cosmosClient.GetAppDataContainer().DeleteItemAsync<IoTDevice>(id, new PartitionKey(userId));
    return Results.Ok();
});

app.MapDefaultEndpoints();

app.Run();

// The IoT device service model used for transmitting data
public record IoTDevice(string Description, string id, string UserId, bool IsOnline = false)
{
    // partition the IoT device by user id
    internal static string UserIdPartitionKey = "/UserId";
}

// Background service used to scaffold the Cosmos DB/Container
public class DatabaseBootstrapper(CosmosClient cosmosClient, ILogger<DatabaseBootstrapper> logger) : BackgroundService, IHealthCheck
{
    private bool _dbCreated;
    private bool _dbCreationFailed;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = _dbCreated
            ? HealthCheckResult.Healthy()
            : _dbCreationFailed
                ? HealthCheckResult.Unhealthy("Database creation failed.")
                : HealthCheckResult.Degraded("Database creation is still in progress.");
        return Task.FromResult(status);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The Cosmos DB emulator can take a very long time to start (multiple minutes) so use a custom resilience strategy
        // to ensure it retries long enough.
        var retry = new ResiliencePipelineBuilder()
            .AddRetry(new()
            {
                Delay = TimeSpan.FromSeconds(5),
                MaxRetryAttempts = 60,
                BackoffType = DelayBackoffType.Constant,
                OnRetry = args =>
                {
                    logger.LogWarning("""
                        Issue during database creation after {AttemptDuration} on attempt {AttemptNumber}. Will retry in {RetryDelay}.
                        Exception:
                            {ExceptionMessage}
                            {InnerExceptionMessage}
                        """,
                        args.Duration,
                        args.AttemptNumber,
                        args.RetryDelay,
                        args.Outcome.Exception?.Message ?? "[none]",
                        args.Outcome.Exception?.InnerException?.Message ?? "");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
        await retry.ExecuteAsync(async ct =>
        {
            await cosmosClient.CreateDatabaseIfNotExistsAsync("iotdb", cancellationToken: ct);
            var database = cosmosClient.GetDatabase("iotdb");
            await database.CreateContainerIfNotExistsAsync(new ContainerProperties("iots", IoTDevice.UserIdPartitionKey), cancellationToken: ct);
            logger.LogInformation("Database successfully created!");
            _dbCreated = true;
        }, stoppingToken);

        _dbCreationFailed = !_dbCreated;
    }
}

// Convenience class for reusing boilerplate code
public static class CosmosClientIoTAppExtensions
{
    public static Container GetAppDataContainer(this CosmosClient cosmosClient)
    {
        var database = cosmosClient.GetDatabase("iotdb");
        var iots = database.GetContainer("iots") ?? throw new ApplicationException("Cosmos DB collection missing.");

        return iots;
    }

    public static async IAsyncEnumerable<TModel> ToAsyncEnumerable<TModel>(this FeedIterator<TModel> setIterator)
    {
        while (setIterator.HasMoreResults)
        {
            foreach (var item in await setIterator.ReadNextAsync())
            {
                yield return item;
            }
        }
    }
}
