# Architecture

## Clean Architecture Layers

```mermaid
graph TD
    subgraph Presentation["Presentation Layer — Web.Api"]
        EP["Endpoints\n(Minimal API)"]
        MW["Middleware\n(Security Headers, Logging,\nRate Limiting, Exception Handler)"]
        AUTH["Auth\n(JWT Bearer, Permission\nAuthorization Policy)"]
    end

    subgraph Application["Application Layer"]
        CMD["Commands & Queries\n(CQRS)"]
        HDL["Command & Query Handlers"]
        VAL["FluentValidation\nValidators"]
        DEC["Decorators\n(ValidationDecorator\nLoggingDecorator)"]
        ABS["Abstractions\n(IApplicationDbContext,\nIFileStorageService,\nICacheService,\nITokenProvider,\nIDownloadTokenService)"]
    end

    subgraph Domain["Domain Layer"]
        USR["User Aggregate\n(User, Role, RefreshToken)"]
        STM["Statement Aggregate\n(Statement, StatementStatus)"]
        DLT["DownloadToken Aggregate"]
        AUD["AuditLog\n(DownloadAuditLog)"]
        EVT["Domain Events"]
    end

    subgraph Infrastructure["Infrastructure Layer"]
        EF["EF Core + PostgreSQL\n(ApplicationDbContext)"]
        RED["Redis Cache\n(CacheService)"]
        FS["Local File Storage\n(LocalFileStorageService)"]
        JWTAUTH["JWT Auth\n(TokenProvider,\nDownloadTokenService,\nPasswordHasher)"]
        PERM["Permission Authorization\n(PermissionProvider,\nPermissionAuthorizationHandler)"]
        LOG["Serilog → Seq"]
    end

    subgraph SharedKernel["Shared Kernel"]
        RES["Result Pattern"]
        ENT["Entity Base Class"]
        IDE["IDomainEvent"]
    end

    Presentation --> Application
    Application --> Domain
    Infrastructure --> Application
    Infrastructure --> Domain
    Domain --> SharedKernel
    Application --> SharedKernel
```

## Kubernetes Infrastructure

```mermaid
graph TB
    Client(["👤 Client / Browser"])

    subgraph cluster["Kubernetes Cluster"]
        subgraph ingress_ns["ingress-nginx namespace"]
            IC["Nginx Ingress Controller\nTLS Termination\nRate Limiting"]
        end

        subgraph ns["secure-statements namespace"]
            subgraph api_layer["Application"]
                SVC["ClusterIP Service\n:8080"]
                POD1["Web API Pod"]
                POD2["Web API Pod"]
                POD3["Web API Pod"]
                HPA["HPA\nmin:2 max:10\nCPU 70% / Mem 80%"]
                PDB["PodDisruptionBudget\nminAvailable: 1"]
            end

            subgraph data_layer["Data"]
                PG["PostgreSQL\nStatefulSet\n:5432"]
                RD["Redis\nDeployment\n:6379"]
                PVC_ST["PVC — Statements\n20Gi RWX"]
                PVC_PG["PVC — Postgres\n10Gi RWO"]
            end

            subgraph obs_layer["Observability"]
                SEQ["Seq\nDeployment\n:80 / :5341"]
                PVC_SEQ["PVC — Seq\n5Gi RWO"]
            end

            subgraph security_layer["Security"]
                SEC["Secrets\n(DB creds, JWT keys)"]
                CM["ConfigMap\n(app settings)"]
                NP["NetworkPolicy\ndefault-deny-all"]
                RBAC["ServiceAccount\nno token automount"]
            end
        end

        subgraph cert_manager["cert-manager"]
            CM_LE["ClusterIssuer\nLet's Encrypt"]
        end
    end

    Client -->|"HTTPS :443"| IC
    IC --> SVC
    SVC --> POD1 & POD2 & POD3
    POD1 & POD2 & POD3 -->|"EF Core"| PG
    POD1 & POD2 & POD3 -->|"StackExchange.Redis"| RD
    POD1 & POD2 & POD3 -->|"Serilog"| SEQ
    POD1 & POD2 & POD3 -->|"IFileStorageService"| PVC_ST
    PG --- PVC_PG
    SEQ --- PVC_SEQ
    HPA -.->|"scales"| POD1 & POD2 & POD3
    PDB -.->|"protects"| POD1 & POD2 & POD3
    IC <-->|"TLS cert"| CM_LE
```

## Request Flow — Download Statement

```mermaid
sequenceDiagram
    participant C as Client
    participant I as Ingress
    participant A as Web API
    participant DB as PostgreSQL
    participant RD as Redis
    participant FS as File Storage (PVC)

    C->>I: POST /statements/{id}/download-tokens
    I->>A: (JWT Bearer validated)
    A->>DB: Validate statement ownership + permissions
    A->>DB: Insert DownloadToken (jti = token ID)
    A-->>C: { downloadUrl, token, expiresAt }

    C->>I: GET /statements/download?token=...
    I->>A: (anonymous — token is the credential)
    A->>A: Validate JWT signature + expiry
    A->>DB: Lookup DownloadToken by jti claim
    A->>DB: Check single-use + not expired
    A->>RD: Check token not in revocation cache
    A->>DB: Mark token as used + insert AuditLog
    A->>FS: Stream PDF file
    A-->>C: application/pdf (streamed)
```

## Security Model

| Layer | Control |
|-------|---------|
| Transport | TLS 1.2+ enforced at Ingress |
| Rate Limiting | Nginx (20 rps) + ASP.NET Core (10/min auth, 100/min API) |
| Authentication | JWT Bearer (HS256, 60-min expiry) |
| Refresh Tokens | SHA-256 hashed, 30-day expiry, rotation on use |
| Download Links | Signed JWT (`jti` = DB row ID), single-use, IP-bound |
| Authorization | Permission-based RBAC (Admin / Customer roles) |
| Network | K8s NetworkPolicy default-deny; pod-to-pod whitelist |
| Secrets | K8s Secrets (recommend Sealed Secrets / Vault in prod) |
| Container | Non-root (UID 1654), read-only filesystem, drop ALL capabilities |
| Security Headers | X-Content-Type-Options, X-Frame-Options, CSP, HSTS |
