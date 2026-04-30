# Local Runbook

## Prerequisites

- Docker Desktop
- PowerShell

## Start Stack

```powershell
docker compose -f infra/docker-compose.yml --profile python-consumer up --build -d
```

For .NET consumer:

```powershell
docker compose -f infra/docker-compose.yml --profile dotnet-consumer up --build -d
```

## Health Checks

```powershell
Invoke-RestMethod http://localhost:8080/healthz
Invoke-RestMethod http://localhost:8080/metrics
Invoke-RestMethod http://localhost:9100/metrics
```

RabbitMQ UI: `http://localhost:15672` (`guest` / `guest`)

## Publish Test Message

```powershell
./scripts/integration-test.ps1 -Message "hello world"
```

## Automated Integration Test

Run integration with Python consumer:

```powershell
./scripts/test-integration.ps1 -Profile python-consumer
```

Run integration with .NET consumer:

```powershell
./scripts/test-integration.ps1 -Profile dotnet-consumer
```

Run the complete local test suite:

```powershell
./scripts/test-all.ps1
```

## View Consumer Logs

```powershell
docker compose -f infra/docker-compose.yml logs consumer-py --tail 100
docker compose -f infra/docker-compose.yml logs consumer-dotnet --tail 100
```

## Stop Stack

```powershell
docker compose -f infra/docker-compose.yml down
```
