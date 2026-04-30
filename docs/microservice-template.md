# Add a New Microservice Template

## Required Files

- `services/<service-name>/Dockerfile`
- Service runtime files (`main.go`, `app.py`, etc.)
- Optional: `requirements.txt` or `go.mod`

## Required Behaviors

- Read config from environment variables.
- Emit structured logs (JSON).
- Expose `healthz` endpoint (for HTTP services) or readiness metric (workers).
- Respect message contract versions when producing/consuming events.

## Integration Steps

1. Add service under `services/`.
2. Add container to `infra/docker-compose.yml`.
3. Add environment variables to `.env.example`.
4. Document startup and test flow in `docs/runbook-local.md`.
5. Add/extend integration test script under `scripts/`.
