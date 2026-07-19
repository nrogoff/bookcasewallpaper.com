using Azure.Storage.Blobs;
using BookshelfWallpaper.Api.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.ConfigureFunctionsApplicationInsights();

        services.AddHttpClient();

        services.AddSingleton(sp =>
        {
            var endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
                ?? throw new InvalidOperationException("COSMOS_ENDPOINT is required.");
            var key = Environment.GetEnvironmentVariable("COSMOS_KEY")
                ?? throw new InvalidOperationException("COSMOS_KEY is required.");
            return new CosmosClient(endpoint, key, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                }
            });
        });

        services.AddSingleton(sp =>
        {
            var connStr = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
                ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING is required.");
            return new BlobServiceClient(connStr);
        });

        services.AddSingleton<ICosmosDbService, CosmosDbService>();
        services.AddSingleton<IBlobStorageService, BlobStorageService>();
        services.AddSingleton<IAudibleService, AudibleService>();
        services.AddSingleton<BookCoverService>();
    })
    .Build();

host.Run();
