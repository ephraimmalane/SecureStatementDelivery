# Monitoring

Self-contained metrics + dashboards for the web-api. The app is already instrumented — it exposes a
Prometheus `/metrics` endpoint (OpenTelemetry exporter) and the pods carry `prometheus.io/scrape`
annotations. This folder adds the *collection and visualization* layer.

Pick **one** of two paths:

## Option A — Standalone Prometheus + Grafana (no operator needed)

Best when the cluster has no monitoring stack. Deploys a minimal single-replica Prometheus and
Grafana in their own `monitoring` namespace.

```bash
kubectl apply -f k8s/monitoring/namespace.yaml
kubectl apply -f k8s/monitoring/networkpolicy-allow-scrape.yaml   # opens web-api:8080 to monitoring
kubectl apply -f k8s/monitoring/prometheus.yaml
kubectl apply -f k8s/monitoring/grafana.yaml

# Edit the Grafana admin password first (Secret grafana-admin → admin-password: CHANGE_ME...).

# Access:
kubectl -n monitoring port-forward svc/grafana 3000:3000     # http://localhost:3000 (admin / <your pw>)
kubectl -n monitoring port-forward svc/prometheus 9090:9090  # http://localhost:9090
```

Prometheus discovers the web-api pods via their annotations, sets `job="web-api"`, and loads the same
recording + alert rules as the operator path (embedded in `prometheus.yaml`). Grafana auto-provisions
the Prometheus datasource and a starter **Secure Statement Delivery — Web API** dashboard
(request rate, 5xx ratio, p95 latency, ingestion, outbox health).

> Do **not** also apply `servicemonitor.yaml` — that's the operator path.

## Option B — kube-prometheus-stack (Prometheus Operator already installed)

Best when the cluster already runs `kube-prometheus-stack`. Skip the standalone Prometheus/Grafana and
let the operator scrape and alert:

```bash
kubectl apply -f k8s/monitoring/servicemonitor.yaml     # tells the operator to scrape web-api
kubectl apply -f k8s/monitoring/prometheus-rules.yaml   # recording + alert rules (PrometheusRule CR)
```

Adjust the `release: kube-prometheus-stack` label in both files to match your Helm release so the
operator selects them. Grafana ships with the stack; import the dashboard JSON from
`grafana.yaml` (the `web-api.json` key) if you want the same starter dashboard.

## What's collected

- **HTTP RED** — rate/errors/duration (ASP.NET Core instrumentation).
- **Runtime** — GC, threads, allocations (.NET runtime instrumentation).
- **Domain metrics** (`StatementMetrics`) — `statements_uploaded_total`,
  `statements_upload_rejected_total{reason}`, `outbox_processed_total`, `outbox_failed_total`.

## Production notes (this is a minimal setup)

- Both Prometheus and Grafana use `emptyDir` — history/prefs are lost on restart. Use PVCs (or a
  managed backend / Grafana Cloud) in production.
- Change the Grafana admin password (and prefer a real secret manager — see the repo's
  External Secrets note).
- Metric names follow the OTel→Prometheus convention (dots → underscores, counters get `_total`);
  histogram bucket names can vary by exporter version — verify against `/metrics` if a panel is empty.
