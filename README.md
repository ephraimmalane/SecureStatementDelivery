# Secure Statement Delivery Platform

A production-grade .NET 10 REST API for secure PDF financial statement management and delivery. Built with Clean Architecture, DDD, CQRS, and JWT-based authentication. Designed for fintech and banking workloads.

## Features

- **Secure PDF Management** — upload, list, retrieve, and revoke financial statements
- **Signed Download Links** — time-limited, single-use JWT tokens with IP binding and full audit trail
- **JWT Authentication** — access tokens (60 min) with refresh token rotation (30 days, SHA-256 hashed)
- **Role-Based Authorization** — Admin (upload, manage, audit) and Customer (read own, download) roles with permission-level granularity
- **Redis Distributed Cache** — permission caching with in-memory fallback when Redis is unavailable
- **Rate Limiting** — fixed-window limiters at both the Nginx and ASP.NET Core layers
- **Audit Logging** — every download attempt (success or failure) is persisted with IP address, user agent, and outcome
- **Structured Logging** — Serilog → Seq with request context enrichment
- **Health Checks** — PostgreSQL and Redis readiness exposed at `/health`
- **Security Headers** — CSP, HSTS, X-Content-Type-Options, X-Frame-Options via middleware

## Architecture

See [docs/architecture.md](docs/architecture.md) for full diagrams including Clean Architecture layers, Kubernetes infrastructure, the download request flow, and the security model.

### Clean Architecture Layers

```
Web.Api (Endpoints, Middleware, Rate Limiting)
    └── Application (Commands, Queries, Handlers, Validators, Decorators)
            ├── Domain (Aggregates: User, Statement, DownloadToken, AuditLog)
            └── Infrastructure (EF Core, Redis, JWT, File Storage, Serilog)
                        SharedKernel (Result Pattern, Entity, Domain Events)
```

### Kubernetes Infrastructure

```
Internet ──HTTPS──► Nginx Ingress (TLS + rate limit)
                          │
                    ClusterIP :8080
                    ┌─────┬─────┐
                  Pod   Pod   Pod   ◄── HPA (2–10 replicas)
                    │     │     │
              ┌─────┼─────┼─────┐
         PostgreSQL  Redis  Seq  PVC(statements)
         StatefulSet  Dep   Dep   RWX 20Gi
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- PostgreSQL 17, Redis 7, Seq 2024.3 (provided via Docker Compose)

### Local Development

```bash
# Start dependencies
docker compose up -d postgres redis seq

# Apply database migrations (runs automatically in Development on startup)
cd src/Web.Api
dotnet run

# API:  http://localhost:5000
# Seq:  http://localhost:8081
```

### Run Tests

```bash
dotnet test SecureStatementDelivery.slnx
```

All architecture tests verify that Clean Architecture layer boundaries are respected.

## Kubernetes Deployment

### Prerequisites

- A Kubernetes cluster (AKS, EKS, GKE, or local k3s/minikube)
- [kubectl](https://kubernetes.io/docs/tasks/tools/)
- [nginx ingress controller](https://kubernetes.github.io/ingress-nginx/deploy/)
- [cert-manager](https://cert-manager.io/docs/installation/)
- A container registry (Docker Hub, ACR, ECR, GCR)

### 1. Build and Push the Image

```bash
docker build -t your-registry/secure-statement-delivery/web-api:1.0.0 \
  -f src/Web.Api/Dockerfile .

docker push your-registry/secure-statement-delivery/web-api:1.0.0
```

Update `image:` in `k8s/web-api/deployment.yaml` to match the pushed tag.

### 2. Configure Secrets

Edit `k8s/secret.yaml` and replace every `CHANGE_ME` placeholder with real values:

| Key | Description |
|-----|-------------|
| `ConnectionStrings__Database` | Full PostgreSQL connection string |
| `ConnectionStrings__Redis` | Redis host:port |
| `Jwt__Secret` | Minimum 32-character signing key |
| `DownloadToken__Secret` | Minimum 32-character signing key |
| `POSTGRES_PASSWORD` | PostgreSQL superuser password |

> **Production recommendation:** Do not commit real secrets to Git. Use [Sealed Secrets](https://github.com/bitnami-labs/sealed-secrets), [HashiCorp Vault](https://www.vaultproject.io/), or the [External Secrets Operator](https://external-secrets.io/) to inject secrets at deploy time.

### 3. Configure the Domain and Storage Class

Update the domain in `k8s/ingress/ingress.yaml`:
```yaml
- host: api.your-domain.com
  # and in tls.hosts:
  - api.your-domain.com
```

Update the `storageClassName` in `k8s/web-api/pvc.yaml` (RWX required for multiple replicas) and `k8s/postgres/statefulset.yaml`. Common choices:

| Cloud | RWO Class | RWX Class |
|-------|-----------|-----------|
| AKS | `managed-csi` | `azurefile-csi` |
| EKS | `gp2` | `efs-sc` |
| GKE | `standard-rwo` | `filestore-sc` |

### 4. Install cert-manager Issuers

```bash
kubectl apply -f k8s/ingress/cert-issuer.yaml
```

Update `email:` in the file first.

### 5. Apply Manifests

Apply in dependency order:

```bash
# Cluster-wide resources
kubectl apply -f k8s/namespace.yaml

# Shared config and security
kubectl apply -f k8s/configmap.yaml
kubectl apply -f k8s/secret.yaml
kubectl apply -f k8s/rbac.yaml
kubectl apply -f k8s/network-policy.yaml

# Data layer
kubectl apply -f k8s/postgres/
kubectl apply -f k8s/redis/

# Observability
kubectl apply -f k8s/seq/

# Application
kubectl apply -f k8s/web-api/

# Ingress
kubectl apply -f k8s/ingress/ingress.yaml
```

### 6. Verify the Deployment

```bash
# All pods running
kubectl get pods -n secure-statements

# Health check
kubectl port-forward svc/web-api 8080:8080 -n secure-statements
curl http://localhost:8080/health

# Logs
kubectl logs -l app.kubernetes.io/name=web-api -n secure-statements --tail=50

# HPA status
kubectl get hpa -n secure-statements
```

## API Reference

### Authentication

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| `POST` | `/auth/register` | Create customer account | — |
| `POST` | `/auth/login` | Login, returns JWT + refresh token | — |
| `POST` | `/auth/refresh` | Rotate refresh token | — |

### Statements

| Method | Path | Description | Permission |
|--------|------|-------------|------------|
| `GET` | `/statements` | List statements (paginated) | `StatementsReadOwn` |
| `GET` | `/statements/{id}` | Get statement details | `StatementsReadOwn` |
| `POST` | `/statements/{id}/download-tokens` | Generate signed download link | `StatementsDownload` |
| `GET` | `/statements/download?token=...` | Stream PDF via token | *(token is credential)* |

### Admin

| Method | Path | Description | Permission |
|--------|------|-------------|------------|
| `GET` | `/admin/audit-logs` | Download audit trail | `AdminAuditLogs` |

### Roles and Permissions

| Permission | Admin | Customer |
|------------|-------|----------|
| `StatementsUpload` | ✅ | — |
| `StatementsReadAny` | ✅ | — |
| `StatementsReadOwn` | ✅ | ✅ |
| `StatementsDownload` | ✅ | ✅ |
| `StatementsRevoke` | ✅ | — |
| `AdminAuditLogs` | ✅ | — |

## Configuration Reference

All settings can be overridden with environment variables using the `__` separator (e.g., `Jwt__Secret`).

| Section | Key | Default | Description |
|---------|-----|---------|-------------|
| `ConnectionStrings` | `Database` | — | PostgreSQL connection string |
| `ConnectionStrings` | `Redis` | — | Redis connection string (optional) |
| `Jwt` | `Secret` | — | HMAC-SHA256 signing key (min 32 chars) |
| `Jwt` | `Issuer` | `secure-statement-delivery` | Token issuer claim |
| `Jwt` | `Audience` | `secure-statement-delivery-clients` | Token audience claim |
| `Jwt` | `ExpirationInMinutes` | `60` | Access token lifetime |
| `Jwt` | `RefreshTokenExpirationDays` | `30` | Refresh token lifetime |
| `DownloadToken` | `Secret` | — | Separate signing key for download JWTs |
| `DownloadToken` | `Issuer` | `statement-download` | Download token issuer |
| `DownloadToken` | `Audience` | `statement-download-clients` | Download token audience |
| `Storage` | `Provider` | `Local` | Storage provider (`Local`) |
| `Storage` | `LocalBasePath` | `storage/statements` | Root path for PDF files |

## Project Structure

```
SecureStatementDelivery/
├── src/
│   ├── Domain/               # Aggregates, domain events, value objects
│   │   ├── Users/            # User, Role, RefreshToken
│   │   ├── Statements/       # Statement, StatementStatus
│   │   ├── DownloadTokens/   # DownloadToken
│   │   └── AuditLogs/        # DownloadAuditLog, AuditAction
│   ├── Application/          # CQRS handlers, validators, abstractions
│   │   ├── Users/            # Login, Register, RefreshToken
│   │   ├── Statements/       # List, GetById, Download, GenerateDownloadLink
│   │   └── Admin/            # GetAuditLogs
│   ├── Infrastructure/       # EF Core, Redis, JWT, file storage, Serilog
│   ├── SharedKernel/         # Result<T>, Entity, IDomainEvent
│   └── Web.Api/              # Minimal API endpoints, middleware, DI
├── tests/
│   └── ArchitectureTests/    # NetArchTest layer boundary enforcement
├── k8s/                      # Kubernetes manifests (production-ready)
│   ├── postgres/
│   ├── redis/
│   ├── seq/
│   ├── web-api/
│   └── ingress/
├── docs/
│   └── architecture.md       # Mermaid architecture diagrams
├── docker-compose.yml        # Local development dependencies
└── SecureStatementDelivery.slnx
```

## Production Checklist

- [ ] Replace all `CHANGE_ME` values in `k8s/secret.yaml`
- [ ] Migrate secrets to Sealed Secrets / Vault / External Secrets Operator
- [ ] Set `storageClassName` in `k8s/web-api/pvc.yaml` and `k8s/postgres/statefulset.yaml`
- [ ] Update `k8s/ingress/ingress.yaml` and `k8s/ingress/cert-issuer.yaml` with real domain and email
- [ ] Push a versioned Docker image and update `k8s/web-api/deployment.yaml`
- [ ] Consider replacing `LocalFileStorageService` with cloud blob storage (Azure Blob, S3, GCS) for multi-replica deployments
- [ ] Add ASP.NET Core Data Protection key persistence (Redis or a dedicated PVC) to support sticky-session-free deployments
- [ ] Configure PostgreSQL connection pooling (PgBouncer) for high-concurrency workloads
- [ ] Set up a PostgreSQL backup schedule (pgBackRest, Velero, or managed DB snapshots)
- [ ] Configure separate liveness (`/health/live`) and readiness (`/health/ready`) endpoints to prevent pod restarts when a dependency is temporarily unavailable
