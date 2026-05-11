# Azure Terraform (Container Apps)

Terraform stacks for running the POC producer and consumer on **Azure Container Apps** with **Key Vault** references and a shared **user-assigned managed identity**.

Layout:

- `modules/container_app`: one Container App (image, scale, env, optional ingress, Key Vault–backed secrets).
- `environments/{dev,staging,prod}`: resource group, Log Analytics, Container Apps environment, Key Vault, secrets, two apps.

See `docs/azure-container-apps.md` for architecture, prerequisites, and GitHub Actions OIDC notes. Image rollouts from GitHub use `/.github/workflows/cd-azure.yml`.

## Quick commands

From an environment directory (example: `dev`):

```powershell
cd c:\Projects\CURSOR\POC\infra\terraform-azure\environments\dev
Copy-Item terraform.tfvars.example terraform.tfvars
# Edit terraform.tfvars (images, RabbitMQ host, credentials, unique key_vault_name).

terraform init
terraform plan
terraform apply
```

Use an Azure subscription where your principal can create resource groups, Key Vaults (RBAC), and Container Apps. The same identity you use for `terraform apply` receives **Key Vault Secrets Officer** on the vault so Terraform can seed `rabbitmq-username` and `rabbitmq-password` secrets.
