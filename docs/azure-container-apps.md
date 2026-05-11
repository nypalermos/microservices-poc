# Azure Container Apps deployment

This document mirrors the AWS ECS baseline (`docs/deployment-cd.md`, `infra/terraform/`) with an Azure-oriented path using **Container Apps**, **Key Vault**, and **managed identity**. Application code stays the same: it still reads `RABBITMQ_HOST`, `RABBITMQ_PORT`, `RABBITMQ_USERNAME`, `RABBITMQ_PASSWORD`, and the other variables described in `docs/security-secrets.md` and `docs/environment-strategy.md`.

## What the Terraform stack creates

For each environment (`infra/terraform-azure/environments/{dev,staging,prod}`):

1. **Resource group** and **Log Analytics** workspace for Container Apps diagnostics.
2. **Container Apps environment** (shared networking/runtime boundary for the apps).
3. **Key Vault** with **RBAC** enabled.
4. **User-assigned managed identity** shared by both Container Apps.
5. **Role assignments**
   - The principal running Terraform: **Key Vault Secrets Officer** (so Terraform can create secrets).
   - The apps identity: **Key Vault Secrets User** (so revisions can resolve Key Vault references).
6. **Key Vault secrets** for RabbitMQ username and password (values come from `terraform.tfvars`; treat that file as sensitive).
7. **Two Container Apps**
   - **Producer**: external HTTP ingress on port **8080** (same as local/ECS). Smoke tests can `POST https://<fqdn>/publish` with JSON `{"message":"..."}`.
   - **Consumer**: no HTTP ingress (worker-style); metrics port is still set in env for parity with other environments.

## RabbitMQ on Azure

This POC does not deploy RabbitMQ inside the same Terraform stack. Point `rabbitmq_host` at any reachable AMQP endpoint (for example a partner-hosted cluster, a VM you operate, or another cloud’s managed broker). The stack sets `RABBITMQ_SCHEME=amqps`, `RABBITMQ_TLS_ENABLED=true`, and `RABBITMQ_PORT` from variables so it matches the cloud-agnostic contract.

## Container images

The examples use placeholder image names (for example GHCR). Azure Container Apps can pull **public** images without extra configuration. For **Azure Container Registry (ACR)**:

1. Build and push images to ACR.
2. Grant the Container Apps identity **AcrPull** on the registry (or use admin credentials only for non-production experiments).
3. Add a `registry` block to the `azurerm_container_app` resource (or extend the shared module) with `server` and `identity`—this repo’s module intentionally omits private registry wiring to keep the first version small; extend it when you adopt ACR.

## Secrets and rotation

- Runtime secrets are **Key Vault references** on the Container App, not plain environment values.
- Terraform still stores secret **values** in state when it creates `azurerm_key_vault_secret`. Prefer a bootstrap process (CI once, or manual portal upload) for production, or use a secrets platform that avoids long-lived values in Terraform state.
- Rotation pattern: update the Key Vault secret version, then create a new Container Apps revision (redeploy) so workloads pick up the new material.

## GitHub Actions and OIDC

The AWS workflow `/.github/workflows/cd.yml` uses OIDC into AWS. This repo includes **`/.github/workflows/cd-azure.yml`**, which logs in with **`azure/login@v2`** (OIDC) and rolls **both** Container Apps to new images via `az containerapp update`, waits for the producer to report `Succeeded`, then runs the same style of **POST** smoke test as the AWS CD workflow.

### Federated identity setup (summary)

1. Create an **App registration** (or reuse one) and add a **federated workload identity credential** for GitHub (for example subject `repo:<org>/<repo>:environment:dev` for the `dev` GitHub Environment).
2. Grant that application access to your subscription or resource group (for example **Contributor** on the resource group that holds the Container Apps; narrower custom roles are possible).
3. Store the application (client) id, tenant id, and subscription id as GitHub Environment secrets used by `cd-azure.yml`.

### Required GitHub Environment secrets (`cd-azure.yml`)

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | App registration client id used with OIDC |
| `AZURE_TENANT_ID` | Azure AD tenant id |
| `AZURE_SUBSCRIPTION_ID` | Target subscription |
| `AZURE_RESOURCE_GROUP` | Resource group containing both Container Apps |
| `CONTAINER_APP_PRODUCER_NAME` | Producer Container App resource name (matches Terraform `name_prefix-producer`) |
| `CONTAINER_APP_CONSUMER_NAME` | Consumer Container App resource name |
| `PRODUCER_IMAGE` | Full image URI including tag (used when workflow input is left empty) |
| `CONSUMER_IMAGE` | Full image URI including tag |
| `PUBLISH_URL` | Full HTTPS URL for `POST /publish` (same contract as `docs/deployment-cd.md`) |

Optional **workflow inputs** `producer_image` and `consumer_image` override the image secrets for a single run (for example to deploy a one-off tag without editing secrets).

Align GitHub **Environments** (`dev`, `staging`, `prod`) with federated credential subjects the same way you do for AWS environment gates.

## Outputs

After `terraform apply`, note:

- `producer_fqdn` / `producer_publish_url` for smoke checks.
- `key_vault_uri` for operational runbooks.
- `managed_identity_principal_id` when assigning extra roles (ACR, Service Bus, etc.).

## Related paths

- Terraform: `infra/terraform-azure/`
- AWS parallel: `infra/terraform/`
- Secrets contract: `docs/security-secrets.md`
