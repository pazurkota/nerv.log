var builder = DistributedApplication.CreateBuilder(args);

var postgresServer = builder.AddPostgres("postgres-server")
    .WithDataVolume()
    .WithPgAdmin();

var postgresDb = postgresServer.AddDatabase("nerv-log-db");

builder.AddDockerfile("nerv-log-server", "..", "nerv.log.Server/Dockerfile")
    .WithHttpEndpoint(port: 8080, targetPort: 8080)
    .WithEnvironment("DOTNET_EnableDiagnostics", "0")
    .WithReference(postgresDb, "DefaultConnection")
    .WaitFor(postgresDb);

builder.Build().Run();