# Monitoring & Dashboards

Observability for the Secure Statement Delivery web-api: metrics flow from the app to Prometheus
to Grafana, both locally (docker-compose) and in the cluster (Kubernetes). Grafana dashboards have
a **single source of truth** shared by both environments.

## Pipeline

```
web-api (OpenTelemetry Prometheus exporter)
   └── GET /metrics            # exposed by ServiceDefaults: MapPrometheusScrapingEndpoint()
        └── Prometheus (scrape job "web-api", every 15s)
             └── Grafana (provisioned datasource "prometheus" + dashboards)
```

Metric names follow the OTel → Prometheus convention: dots become underscores and counters get a
`_total` suffix. For example the app meter `statements.revoked` is queried as
`statements_revoked_total`. Always filter on `{job="web-api"}` — that label is set by the scrape
config and is what the dashboard panels and recording rules key off.

## Single source of truth: the dashboard JSON

Both environments load the **same files**:

```
k8s/monitoring/dashboards/
  ├── web-api.json            # HTTP rate, 5xx SLO, p95 latency, ingestion, outbox
  └── statements-revoked.json # revocation count / rate / vs uploads
```

Edit a dashboard once here and it flows to both local and cluster Grafana. They live under
`k8s/monitoring/` because kustomize's `configMapGenerator` can only reference files inside the
kustomization root — that constraint is what pins the canonical location.

> Tip: to edit visually, open Grafana, change the panels, then **Dashboard settings → JSON Model**,
> copy it back into the corresponding file. Keep `"version"` and `"uid"` stable.

## View it locally (docker-compose)

```bash
docker compose up -d
```

- **Grafana:** http://localhost:3000 — dashboards are under the *Secure Statement Delivery* folder.
  Anonymous viewing is enabled (no login); use `admin` / `admin` to edit.
- **Prometheus:** http://localhost:9090

Local wiring lives in `monitoring/local/`:

| File | Purpose |
|------|---------|
| `prometheus.yml` | Scrape config — targets `web-api:8080/metrics`, job `web-api`. |
| `rules.yml` | Recording rules (error ratio, p95) mirroring the cluster's `PrometheusRule`, so the web-api dashboard's SLO panels render locally. |
| `grafana-datasources.yml` | Grafana datasource → `http://prometheus:9090` (uid `prometheus`). |

The Grafana **dashboard provider** (`provisioning/dashboards.yaml`) and the **dashboard JSON** are
mounted directly from `k8s/monitoring/` — no local copies.

> The revocation panels stay at zero until the `statements_revoked_total` counter is first
> incremented (revoke a statement). Counters aren't emitted before their first increment — expected,
> not a misconfiguration.

## Deploy to the cluster (Kubernetes)

The monitoring stack is a kustomize package — apply it with `-k`, not `-f`:

```bash
kubectl apply -k k8s/monitoring
```

`k8s/monitoring/kustomization.yaml` generates the `grafana-provisioning` ConfigMap from
`provisioning/*.yaml` + `dashboards/*.json`. The cluster datasource points at the in-cluster
Prometheus (`http://prometheus.monitoring.svc:9090`); alerts and SLO recording rules are the
`PrometheusRule` in `prometheus-rules.yaml` (requires the Prometheus Operator / kube-prometheus-stack).

Access Grafana:

```bash
kubectl -n monitoring port-forward svc/grafana 3000:3000   # then http://localhost:3000
```

The generated ConfigMap keeps a stable name (`disableNameSuffixHash: true`), so **dashboard changes
need a manual rollout** to take effect:

```bash
kubectl apply -k k8s/monitoring
kubectl rollout restart deployment/grafana -n monitoring
```

## File layout

```
monitoring/
  README.md                      # this file
  local/
    prometheus.yml               # local scrape config
    rules.yml                    # local recording rules (mirror of the k8s PrometheusRule)
    grafana-datasources.yml      # local datasource (prometheus:9090)

k8s/monitoring/
  kustomization.yaml             # generates grafana-provisioning ConfigMap from the files below
  grafana.yaml                   # Grafana Secret + Deployment + Service
  prometheus.yaml                # cluster Prometheus
  prometheus-rules.yaml          # alerts + SLO recording rules (PrometheusRule CRD)
  servicemonitor.yaml            # scrape config for the Operator
  networkpolicy-allow-scrape.yaml
  provisioning/
    datasources.yaml             # cluster datasource (prometheus.monitoring.svc)
    dashboards.yaml              # dashboard provider (SHARED with local)
  dashboards/                    # ← canonical dashboards (SHARED with local)
    web-api.json
    statements-revoked.json
```

## Adding a new dashboard

1. Add `k8s/monitoring/dashboards/<name>.json` (unique `uid`, `"version": 1`).
2. Add it to the `configMapGenerator.files` list and a matching `subPath` volumeMount in
   `k8s/monitoring/grafana.yaml`.
3. Add a `subPath` mount for it under the `grafana` service in `docker-compose.override.yml`
   (the local provider auto-discovers any `*.json` in the mounted dashboards directory, so mounting
   the whole `k8s/monitoring/dashboards` directory already covers it).
