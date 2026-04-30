# Environment Strategy

## Environment Model

Use four lifecycle environments with promotion gates:

- `local`: developer workstation using Docker Compose.
- `dev`: shared integration environment for fast iteration.
- `staging`: production-like validation environment with release candidate builds.
- `prod`: live environment with strict change control.

## Configuration Parity Rules

- All services use the same canonical environment variable contract.
- Environment differences are configuration-only (no code forks).
- Secrets are injected by the platform secret backend and never committed.

## Env File Templates

Environment templates are in `infra/environments/`:

- `local.env.example`
- `dev.env.example`
- `staging.env.example`
- `prod.env.example`

Create real environment files from templates, for example:

```powershell
Copy-Item infra/environments/local.env.example infra/environments/local.env
```

## Local Run with Explicit Env File

```powershell
$env:ENV_FILE = "../infra/environments/local.env"
docker compose -f infra/docker-compose.yml --profile python-consumer up --build -d
```

## Promotion Flow

1. Merge to main after CI passes.
2. Deploy to `dev` with environment-specific secrets/config.
3. Promote same artifact to `staging` and run smoke/integration checks.
4. Promote same artifact to `prod` with approval gate and rollback plan.

The artifact (container image digest) must remain unchanged across promotions.

## Production Guardrails

- `RABBITMQ_TLS_ENABLED=true` in staging/prod.
- Use `amqps` and managed broker endpoints outside local.
- Rotate credentials regularly through secret manager.
- Keep `MAX_RETRIES`, `RETRY_DELAY_MS`, and queue names explicit per environment.
