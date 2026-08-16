// .NET Aspire orchestrator for Secure Statement Delivery.
// Spins up PostgreSQL, Redis, Seq, and Keycloak as containers and runs the Web API as a
// local process, injecting connection strings and configuration so the existing application
// code (manual EF Core / Redis / JWT wiring) works unchanged.
var builder = DistributedApplication.CreateBuilder(args);

// --- Secrets / parameters --------------------------------------------------------------
// Dev defaults live in appsettings.json (Parameters section). For real use, override them
// with user-secrets:  dotnet user-secrets set "Parameters:download-token-secret" "<value>"
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakClientSecret = builder.AddParameter("keycloak-client-secret", secret: true);
var downloadTokenSecret = builder.AddParameter("download-token-secret", secret: true);

// --- Data services ---------------------------------------------------------------------
// The database resource is named "Database" so Aspire injects ConnectionStrings__Database,
// which is exactly what the application reads (GetConnectionString("Database")).
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    // Runs a pgAdmin container already wired to this server — open it from the dashboard,
    // no manual connection setup or generated-password lookup needed.
    .WithPgAdmin();
var database = postgres.AddDatabase("Database", databaseName: "secure_statements");

// Named "Redis" so the injected connection string lands at ConnectionStrings__Redis.
var redis = builder.AddRedis("Redis")
    .WithDataVolume();

var seq = builder.AddSeq("seq")
    .WithDataVolume();

// --- Identity provider -----------------------------------------------------------------
// Realm import substitutes ${KEYCLOAK_CLIENT_SECRET} from the environment, so the realm's
// client secret matches the value the API uses.
var keycloak = builder.AddKeycloak("keycloak", port: 8080, adminPassword: keycloakAdminPassword)
    .WithDataVolume()
    .WithEnvironment("KEYCLOAK_CLIENT_SECRET", keycloakClientSecret)
    // Dev reuses the same secret for the M2M ingestion (statement-generator) client; production
    // provisions a distinct KEYCLOAK_INGEST_CLIENT_SECRET (see k8s/secret.yaml).
    .WithEnvironment("KEYCLOAK_INGEST_CLIENT_SECRET", keycloakClientSecret)
    .WithRealmImport("../../keycloak");

// --- Web API ---------------------------------------------------------------------------
builder.AddProject<Projects.Web_Api>("web-api")
    .WithReference(database).WaitFor(database)
    .WithReference(redis).WaitFor(redis)
    .WithReference(seq).WaitFor(seq)
    .WithReference(keycloak).WaitFor(keycloak)
    // Keycloak issues tokens with a FIXED frontend issuer (https://localhost:8080) but its
    // discovery document reflects whatever URL is used to fetch it. If the API validates via
    // an internal Aspire URL, the discovered issuer won't match the token's iss → invalid_token.
    // Pin BaseUrl to the same public URL the issuer uses so issuance and validation agree.
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
