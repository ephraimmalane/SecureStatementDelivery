var builder = DistributedApplication.CreateBuilder(args);

var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloakClientSecret = builder.AddParameter("keycloak-client-secret", secret: true);
var downloadTokenSecret = builder.AddParameter("download-token-secret", secret: true);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var database = postgres.AddDatabase("Database", databaseName: "secure_statements");

var redis = builder.AddRedis("Redis")
    .WithDataVolume()
    .WithRedisInsight();

var seq = builder.AddSeq("seq")
    .WithDataVolume();

var keycloak = builder.AddKeycloak("keycloak", port: 8080, adminPassword: keycloakAdminPassword)
    .WithDataVolume()
    .WithEnvironment("KEYCLOAK_CLIENT_SECRET", keycloakClientSecret)
    .WithEnvironment("KEYCLOAK_INGEST_CLIENT_SECRET", keycloakClientSecret)
    .WithRealmImport("../../keycloak");

// HashiCorp Vault dev server: exercises the app's VaultConfigurationProvider path locally.
// Dev mode auto-unseals and mounts a KV v2 engine at "secret/". The root token is fixed so the
// seed job and the web-api can authenticate deterministically.
var vault = builder.AddContainer("vault", "hashicorp/vault", "1.18")
    .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", "root")
    .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", "0.0.0.0:8200")
    .WithArgs("server", "-dev")
    .WithHttpEndpoint(port: 8200, targetPort: 8200, name: "http");

// One-shot job: wait for Vault, then write the app-owned secrets to the exact KV v2 path the
// provider reads (mount "secret", path "secure-statement-delivery"). Values mirror what is already
// injected elsewhere, so Vault becomes the source without changing effective config. Connection
// strings are intentionally NOT seeded — Aspire injects those dynamically and Vault wins on conflict.
var vaultSeed = builder.AddContainer("vault-seed", "hashicorp/vault", "1.18")
    .WithEntrypoint("/bin/sh")
    .WithEnvironment("VAULT_ADDR", vault.GetEndpoint("http"))
    .WithEnvironment("VAULT_TOKEN", "root")
    .WithEnvironment("KEYCLOAK_CLIENT_SECRET", keycloakClientSecret)
    .WithEnvironment("DOWNLOAD_TOKEN_SECRET", downloadTokenSecret)
    .WithEnvironment("FIELD_ENCRYPTION_KEY", "ZGV2LWZpZWxkLWVuY3J5cHRpb24ta2V5LTMyYnl0ZXM=")
    .WithArgs("-c", """
        until vault status >/dev/null 2>&1; do echo 'waiting for vault...'; sleep 1; done
        vault kv put secret/secure-statement-delivery \
          "Keycloak:ClientSecret=$KEYCLOAK_CLIENT_SECRET" \
          "DownloadToken:Secret=$DOWNLOAD_TOKEN_SECRET" \
          "FieldEncryption:Key=$FIELD_ENCRYPTION_KEY"
        """)
    // One-shot seed job: hidden from the dashboard so its (expected) Exited-0 terminal state does
    // not read as a failure. It still runs and still gates the web-api via WaitForCompletion below.
    .WithInitialState(new Aspire.Hosting.ApplicationModel.CustomResourceSnapshot
    {
        ResourceType = "container",
        State = Aspire.Hosting.ApplicationModel.KnownResourceStates.Starting,
        Properties = [],
        IsHidden = true,
    })
    .WaitFor(vault);

// Prometheus scrapes the web-api /metrics endpoint (job "web-api") using the Aspire-specific
// config, and shares the recording rules with the docker-compose/k8s setups.
var prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v2.54.1")
    .WithBindMount("../../monitoring/aspire/prometheus.yml", "/etc/prometheus/prometheus.yml", isReadOnly: true)
    .WithBindMount("../../monitoring/local/rules.yml", "/etc/prometheus/rules.yml", isReadOnly: true)
    .WithArgs("--config.file=/etc/prometheus/prometheus.yml")
    .WithHttpEndpoint(port: 9090, targetPort: 9090, name: "http");

// Grafana with anonymous viewing enabled; provisions the "prometheus" datasource and the
// dashboards that are the single source of truth shared with docker-compose and k8s.
builder.AddContainer("grafana", "grafana/grafana", "11.2.0")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
    .WithEnvironment("GF_USERS_ALLOW_SIGN_UP", "false")
    .WithEnvironment("GF_ANALYTICS_REPORTING_ENABLED", "false")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ENABLED", "true")
    .WithEnvironment("GF_AUTH_ANONYMOUS_ORG_ROLE", "Viewer")
    .WithBindMount("../../monitoring/aspire/grafana-datasources.yml", "/etc/grafana/provisioning/datasources/datasources.yml", isReadOnly: true)
    .WithBindMount("../../k8s/monitoring/provisioning/dashboards.yaml", "/etc/grafana/provisioning/dashboards/dashboards.yml", isReadOnly: true)
    .WithBindMount("../../k8s/monitoring/dashboards", "/var/lib/grafana/dashboards", isReadOnly: true)
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http")
    .WaitFor(prometheus);

// Static demo console served by nginx. Port 8000 is one of the origins whitelisted in the API's
// Cors:AllowedOrigins (appsettings.Development.json), so the browser can call the web-api. The UI is
// framework-free static files, mounted read-only and kept separate from the backend build.
builder.AddContainer("demo-ui", "nginx", "1.27-alpine")
    .WithBindMount("../../demo-ui", "/usr/share/nginx/html", isReadOnly: true)
    .WithHttpEndpoint(port: 8000, targetPort: 80, name: "http");

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
    .WithEnvironment("Vault__Address", vault.GetEndpoint("http"))
    .WithEnvironment("Vault__Token", "root")
    .WithEnvironment("Serilog__WriteTo__1__Args__ServerUrl", seq.GetEndpoint("http"))
    .WaitForCompletion(vaultSeed)
    .WithExternalHttpEndpoints();

builder.Build().Run();
