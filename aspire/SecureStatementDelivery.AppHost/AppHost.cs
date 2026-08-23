var builder = DistributedApplication.CreateBuilder(args);

var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakClientSecret = builder.AddParameter("keycloak-client-secret", secret: true);
var downloadTokenSecret = builder.AddParameter("download-token-secret", secret: true);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var database = postgres.AddDatabase("Database", databaseName: "secure_statements");

var redis = builder.AddRedis("Redis")
    .WithDataVolume();

var seq = builder.AddSeq("seq")
    .WithDataVolume();

var keycloak = builder.AddKeycloak("keycloak", port: 8080, adminPassword: keycloakAdminPassword)
    .WithDataVolume()
    .WithEnvironment("KEYCLOAK_CLIENT_SECRET", keycloakClientSecret)
    .WithEnvironment("KEYCLOAK_INGEST_CLIENT_SECRET", keycloakClientSecret)
    .WithRealmImport("../../keycloak");

builder.AddProject<Projects.Web_Api>("web-api")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithReference(seq).WaitFor(seq)
    .WithReference(keycloak).WaitFor(keycloak)
    .WithEnvironment("Keycloak__BaseUrl", "https://localhost:8080")
    .WithEnvironment("Keycloak__Realm", "secure-statements")
    .WithEnvironment("Keycloak__ClientId", "secure-statement-delivery")
    .WithEnvironment("Keycloak__ClientSecret", keycloakClientSecret)
    .WithEnvironment("Keycloak__AdminUsername", "admin")
    .WithEnvironment("Keycloak__AdminPassword", keycloakAdminPassword)
    .WithEnvironment("DownloadToken__Secret", downloadTokenSecret)
    .WithEnvironment("Serilog__WriteTo__1__Args__ServerUrl", seq.GetEndpoint("http"))
    .WithExternalHttpEndpoints();

builder.Build().Run();
