# Producer/Consumer Microservice POC

Cloud-agnostic, containerized event-driven POC:
- Go producer publishes string-based JSON messages to RabbitMQ.
- Python consumer or .NET consumer processes messages with retry and DLQ behavior.
- Docker Compose orchestrates the local stack.

## Quick Start

```powershell
docker compose -f infra/docker-compose.yml --profile python-consumer up --build -d
./scripts/integration-test.ps1 -Message "hello world"
docker compose -f infra/docker-compose.yml logs consumer-py --tail 50
```

Run with .NET consumer instead:

```powershell
docker compose -f infra/docker-compose.yml --profile dotnet-consumer up --build -d
docker compose -f infra/docker-compose.yml logs consumer-dotnet --tail 50
```

Use an explicit environment file (recommended):

```powershell
$env:ENV_FILE = "../infra/environments/local.env"
docker compose -f infra/docker-compose.yml --profile python-consumer up --build -d
```

## Testing

Run full local test flow (build/syntax + integration for both consumers):

```powershell
./scripts/test-all.ps1
```

Generate coverage artifacts only:

```powershell
./scripts/test-coverage.ps1
```

Coverage output is written under `coverage/go`, `coverage/python`, and `coverage/dotnet`.

Run only integration test for one consumer profile:

```powershell
./scripts/test-integration.ps1 -Profile python-consumer
./scripts/test-integration.ps1 -Profile dotnet-consumer
```

## CI

- GitHub Actions workflow: `.github/workflows/ci.yml`
- Runs on push/PR:
  - unit tests for Go, Python, and .NET
  - integration test matrix for `python-consumer` and `dotnet-consumer`
  - coverage artifact generation and upload
  - secret scanning with gitleaks

## CD

- GitHub Actions deployment workflow: `.github/workflows/cd.yml`
- Azure Container Apps image rollout (OIDC): `.github/workflows/cd-azure.yml`
- Uses staged environments (`dev`, `staging`, `prod`) with GitHub Environment approvals.
- Deployment and promotion details: `docs/deployment-cd.md`
- Azure Container Apps baseline (Terraform + Key Vault): `docs/azure-container-apps.md`

## Repository Layout

- `services/producer-go`: Go producer service.
- `services/consumer-py`: Python consumer service.
- `services/consumer-dotnet`: .NET consumer service.
- `infra/docker-compose.yml`: local orchestration.
- `infra/environments`: local/dev/staging/prod environment templates.
- `infra/examples`: deployment examples for secrets injection.
- `infra/terraform`: AWS IaC modules and environment stacks.
- `infra/terraform-azure`: Azure Container Apps modules and environment stacks.
- `contracts/message.schema.json`: inbound RabbitMQ message schema.
- `contracts/consumed-event.schema.json`: outbound Kafka consumed-event schema.
- `docs/`: architecture, runbook, cloud path, and expansion template.

## Security and Secrets

- Canonical broker secret keys are documented in `docs/security-secrets.md`.
- Environment model and promotion strategy are documented in `docs/environment-strategy.md`.
- Services support both:
  - direct secret URL: `RABBITMQ_URL`
  - component-based secrets: `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`, `RABBITMQ_VHOST`, `RABBITMQ_TLS_ENABLED`
- Example secret injection manifests:
  - `infra/examples/aws-ecs-secrets.example.json`
  - `infra/examples/k8s-external-secrets.example.yaml`

## Terraform/OpenTofu

- **AWS** environment stacks:
  - `infra/terraform/environments/dev`
  - `infra/terraform/environments/staging`
  - `infra/terraform/environments/prod`
- **Azure** environment stacks:
  - `infra/terraform-azure/environments/dev`
  - `infra/terraform-azure/environments/staging`
  - `infra/terraform-azure/environments/prod`
- Each stack includes `terraform.tfvars.example` for required inputs.
- AWS deployment baseline: `docs/deployment-cd.md`
- Azure Container Apps baseline: `docs/azure-container-apps.md`

## Core Endpoints

- Producer health: `GET /healthz`
- Producer metrics: `GET /metrics`
- Publish event: `POST /publish`
- Consumer metrics: `GET :9100/metrics`
