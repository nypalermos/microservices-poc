# Cloud Path (AWS-Preferred, Cloud-Agnostic)

## Principles

- Keep business logic independent from infrastructure SDKs.
- Use configuration to select broker endpoints and queue names.
- Preserve container contract across environments.

## Target Progression

1. **Local**: Docker Compose + RabbitMQ container.
2. **AWS first**: ECS/Fargate for producer/consumer containers.
3. **Managed broker**: Amazon MQ (RabbitMQ) for protocol parity.
4. **Future broker swap**: SQS/SNS or Kafka by replacing adapter layer, not core business flow.

## Suggested Infra Modules

- Container image registry (`ECR` on AWS, equivalent elsewhere).
- Compute runtime (`ECS/Fargate`, or Kubernetes if needed later).
- Broker module (Amazon MQ now, pluggable for alternate clouds).
- Secret/config module (`SSM Parameter Store` or secrets equivalent).
- Observability module (logs + metrics + alerting).

## Portability Guardrails

- Do not embed AWS SDK calls in message processing logic.
- Keep queue/exchange names and routing keys in environment variables.
- Keep contract schema versioned and validated in CI.
- Ship health and metrics endpoints with each service.
