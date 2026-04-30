# Security and Secrets (Cloud-Agnostic)

## Goals

- Keep service code independent of cloud-specific SDKs.
- Keep secret names stable across environments.
- Support AWS first, but allow swapping secret backends later.

## Canonical Secret Contract

Services resolve broker config in this order:
1. `RABBITMQ_URL` (single secret URL)
2. component secrets/env values:
   - `RABBITMQ_SCHEME`
   - `RABBITMQ_HOST`
   - `RABBITMQ_PORT`
   - `RABBITMQ_USERNAME`
   - `RABBITMQ_PASSWORD`
   - `RABBITMQ_VHOST`
   - `RABBITMQ_TLS_ENABLED`

This keeps the application portable while allowing each platform to inject secrets differently.

## Local Development

- Keep local defaults in `.env.example`.
- Never commit real credentials.
- Use `docker compose` env injection for local-only credentials.

## AWS-Preferred Deployment

- Store secrets in AWS Secrets Manager or SSM Parameter Store.
- Inject secrets into ECS task definitions as env vars.
- Keep application code unchanged between local and AWS.

Reference example: `infra/examples/aws-ecs-secrets.example.json`

## Cloud-Agnostic Kubernetes Pattern

- Use External Secrets Operator.
- Map provider secret values to the canonical env key names.
- Deployment manifests stay the same while backend provider changes.

Reference example: `infra/examples/k8s-external-secrets.example.yaml`

## Hardening Checklist

- Use TLS (`amqps`) in non-local environments.
- Rotate `RABBITMQ_USERNAME`/`RABBITMQ_PASSWORD` regularly.
- Restrict secret read permissions with least privilege IAM.
- Never log secret values.
- Add automated secret scanning in CI (for example: `gitleaks`).
