using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

#pragma warning disable ASPIRECOSMOSDB001
var cosmos = builder.AddAzureCosmosDB("cosmos")
.RunAsPreviewEmulator(emulator =>
    {
        emulator
            .WithContainerName("myservice-cosmosdb")
            .WithDataExplorer(1234)
            .WithOtlpExporter()
            .WithDataVolume()
            .WithLifetime(ContainerLifetime.Persistent);
    });
#pragma warning restore ASPIRECOSMOSDB001

var iotdb = cosmos.AddCosmosDatabase("iotdb");
var userDb = cosmos.AddCosmosDatabase("userdb");
var usersContainer = userDb.AddContainer("container", "/id");


var apiService = builder.AddProject<Projects.AspireWithCosmos_ApiService>("apiservice")
    .WithReference(iotdb);

builder.AddProject<Projects.AspireWithCosmos_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
