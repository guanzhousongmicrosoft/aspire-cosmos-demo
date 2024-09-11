using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var iotdb = builder.AddAzureCosmosDB("cosmos").AddDatabase("iotdb")
    // Remove the RunAsEmulator() line should you want to use a live instance during development
    .RunAsEmulator(); 

var apiService = builder.AddProject<Projects.AspireWithCosmos_ApiService>("apiservice")
    .WithReference(iotdb);

builder.AddProject<Projects.AspireWithCosmos_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithReference(apiService);

builder.Build().Run();
