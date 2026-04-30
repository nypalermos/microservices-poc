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

## Repository Layout

- `services/producer-go`: Go producer service.
- `services/consumer-py`: Python consumer service.
- `services/consumer-dotnet`: .NET consumer service.
- `infra/docker-compose.yml`: local orchestration.
- `infra/examples`: deployment examples for secrets injection.
- `contracts/message.schema.json`: event schema contract.
- `docs/`: architecture, runbook, cloud path, and expansion template.

## Security and Secrets

- Canonical broker secret keys are documented in `docs/security-secrets.md`.
- Services support both:
  - direct secret URL: `RABBITMQ_URL`
  - component-based secrets: `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`, `RABBITMQ_VHOST`, `RABBITMQ_TLS_ENABLED`
- Example secret injection manifests:
  - `infra/examples/aws-ecs-secrets.example.json`
  - `infra/examples/k8s-external-secrets.example.yaml`

## Core Endpoints

- Producer health: `GET /healthz`
- Producer metrics: `GET /metrics`
- Publish event: `POST /publish`
- Consumer metrics: `GET :9100/metrics`
