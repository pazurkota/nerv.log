var builder = DistributedApplication.CreateBuilder(args);

var postgresServer = builder.AddPostgres("postgres-server")
    .WithDataVolume()
    .WithPgAdmin();

var postgresDb = postgresServer.AddDatabase("nerv-log-db");

var server = builder.AddDockerfile("nerv-log-server", 
        "../", "../nerv.log.Server/Dockerfile")
    .WithReference(postgresDb);

builder.Build().Run();