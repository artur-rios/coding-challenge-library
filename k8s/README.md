# Kubernetes manifests

Plain YAML rather than a Helm chart. The deployment has one workload, one Service, one ConfigMap and
one Secret; templating four files that nothing else consumes would add a layer without removing one.

```bash
kubectl apply -k k8s/
```

Nothing here needs a real cluster to be reviewed, and none of it is applied by the test suite. It is
included because the challenge asks how the API stays correct between 2 and 11 replicas, and these
files are where most of that answer is actually enforced.

## Files

| File                | What it holds                                                              |
|---------------------|----------------------------------------------------------------------------|
| `namespace.yaml`    | The namespace everything else is scoped to.                                 |
| `configmap.yaml`    | Non-secret configuration: environment, OTLP endpoint, idempotency retention. |
| `secret.yaml`       | Placeholder credentials. **No real value is committed** - see below.         |
| `migration-job.yaml`| A one-shot Job that brings the schema up to date before any replica starts.  |
| `deployment.yaml`   | The API: replicas, probes, resource requests and limits.                     |
| `service.yaml`      | The ClusterIP Service in front of the replicas.                              |
| `hpa.yaml`          | Horizontal scaling between 2 and 11 replicas.                                |
| `kustomization.yaml`| Applies the above in one command.                                            |

## Secrets

`secret.yaml` contains `CHANGE_ME` placeholders and is committed only so the shape of the Secret is
reviewable. Supply the real values out of band:

```bash
kubectl create secret generic library-api-secrets \
  --namespace library \
  --from-literal=LIBRARY_DATA_CONNECTIONSTRING='Host=...;Port=5432;Database=library;Username=...;Password=...' \
  --from-literal=LIBRARY_REDIS_CONNECTIONSTRING='redis:6379' \
  --dry-run=client -o yaml | kubectl apply -f -
```

In a real deployment this is where an External Secrets Operator or a CSI secret driver belongs, so
the value never exists as a Kubernetes object anybody can read.

## Why the schema is migrated by a Job

Every replica applying migrations on start-up is safe in the sense that EF Core takes an advisory
lock, so they cannot corrupt one another. It is still the wrong design: every replica waits on the
first, and a migration that fails turns into a crash loop across the whole deployment rather than one
failed Job with a readable log. The Job runs first, exits, and only then do the replicas start with
`LIBRARY_RUN_MIGRATIONS` unset.
