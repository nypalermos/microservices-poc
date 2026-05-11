# Deployment and CD Baseline

## Goal

Provide a staged deployment baseline that promotes the same artifact through:

1. `dev`
2. `staging`
3. `prod`

## GitHub Environments

Create GitHub environments:

- `dev`
- `staging`
- `prod`

Set required reviewers on `staging` and `prod` for approval gates.

## Required Environment Secrets

Configure these secrets per environment:

- `AWS_DEPLOY_ROLE_ARN`
- `AWS_REGION`
- `ECR_REGISTRY`
- `ECS_CLUSTER`
- `ECS_SERVICE_PRODUCER`
- `ECS_SERVICE_CONSUMER`
- `PUBLISH_URL`

## Workflow

`/.github/workflows/cd.yml` is manually triggered with `target_env` input:

- Assumes AWS role using OIDC.
- Triggers ECS service deployment (`force-new-deployment`).
- Waits for services to become stable.
- Runs smoke publish test against `PUBLISH_URL`.

For **Azure Container Apps**, use `/.github/workflows/cd-azure.yml` and the GitHub Environment secrets listed in `docs/azure-container-apps.md` (same `PUBLISH_URL` smoke contract).

## Promotion Pattern

- Promote only after CI green.
- Reuse the same image digest from `dev` -> `staging` -> `prod`.
- Roll back by redeploying previous known-good task definition/image.

## IaC Baseline

Terraform/OpenTofu structure:

- `infra/terraform/modules/ecs_service`
- `infra/terraform/environments/dev`
- `infra/terraform/environments/staging`
- `infra/terraform/environments/prod`

Current module is an intentionally minimal baseline placeholder (`null_resource`) to define the interface and promote consistent environment structure before wiring full AWS networking/ECS resources.

The baseline now includes real ECS task/service resources with variable-driven environment stacks. Fill each environment's `terraform.tfvars` from the provided examples:

- `infra/terraform/environments/dev/terraform.tfvars.example`
- `infra/terraform/environments/staging/terraform.tfvars.example`
- `infra/terraform/environments/prod/terraform.tfvars.example`

Recommended first run in `dev`:

```powershell
Copy-Item infra/terraform/environments/dev/terraform.tfvars.example infra/terraform/environments/dev/terraform.tfvars
cd infra/terraform/environments/dev
terraform init
terraform validate
terraform plan
```
