terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = ">= 3.114.0, < 5.0.0"
    }
  }
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy    = true
      recover_soft_deleted_key_vaults = true
    }
  }
}

data "azurerm_client_config" "current" {}

locals {
  base_env = {
    EXCHANGE_NAME        = "poc.events"
    DLX_NAME             = "poc.dlx"
    QUEUE_NAME           = "poc.queue"
    RETRY_QUEUE_NAME     = "poc.queue.retry"
    DLQ_NAME             = "poc.queue.dlq"
    ROUTING_KEY          = "poc.message"
    RETRY_DELAY_MS       = "5000"
    MAX_RETRIES          = "3"
    RABBITMQ_HOST        = var.rabbitmq_host
    RABBITMQ_PORT        = tostring(var.rabbitmq_port)
    RABBITMQ_SCHEME      = "amqps"
    RABBITMQ_TLS_ENABLED = "true"
    RABBITMQ_VHOST       = "/"
  }

  kv_secret_identity_id = azurerm_user_assigned_identity.apps.id

  app_secrets = {
    rabbitmq-username = {
      key_vault_secret_id = azurerm_key_vault_secret.rabbitmq_username.versionless_id
      identity_id         = local.kv_secret_identity_id
    }
    rabbitmq-password = {
      key_vault_secret_id = azurerm_key_vault_secret.rabbitmq_password.versionless_id
      identity_id         = local.kv_secret_identity_id
    }
  }
}

resource "azurerm_resource_group" "main" {
  name     = var.resource_group_name
  location = var.location
}

resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.name_prefix}-law"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
}

resource "azurerm_container_app_environment" "main" {
  name                       = "${var.name_prefix}-cae"
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
}

resource "azurerm_user_assigned_identity" "apps" {
  name                = "${var.name_prefix}-apps-mi"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
}

resource "azurerm_key_vault" "main" {
  name                       = var.key_vault_name
  location                   = azurerm_resource_group.main.location
  resource_group_name        = azurerm_resource_group.main.name
  tenant_id                  = data.azurerm_client_config.current.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true

  purge_protection_enabled = var.purge_protection_enabled
  soft_delete_retention_days = var.soft_delete_retention_days
}

resource "azurerm_role_assignment" "terraform_kv_secrets_officer" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "apps_kv_secrets_user" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}

resource "azurerm_key_vault_secret" "rabbitmq_username" {
  name         = "rabbitmq-username"
  value        = var.rabbitmq_username
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.terraform_kv_secrets_officer]
}

resource "azurerm_key_vault_secret" "rabbitmq_password" {
  name         = "rabbitmq-password"
  value        = var.rabbitmq_password
  key_vault_id = azurerm_key_vault.main.id

  depends_on = [azurerm_role_assignment.terraform_kv_secrets_officer]
}

module "producer" {
  source = "../../modules/container_app"

  depends_on = [
    azurerm_role_assignment.apps_kv_secrets_user,
    azurerm_key_vault_secret.rabbitmq_username,
    azurerm_key_vault_secret.rabbitmq_password,
  ]

  name                         = "${var.name_prefix}-producer"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id

  min_replicas = var.producer_min_replicas
  max_replicas = var.producer_max_replicas

  container_name = "producer"
  image            = var.producer_image
  cpu              = var.producer_cpu
  memory           = var.producer_memory

  environment_variables = merge(local.base_env, {
    PRODUCER_PORT = "8080"
  })

  secret_environment_variables = {
    RABBITMQ_USERNAME = "rabbitmq-username"
    RABBITMQ_PASSWORD = "rabbitmq-password"
  }

  secrets = local.app_secrets

  ingress = {
    external_enabled = true
    target_port      = 8080
    transport        = "auto"
  }
}

module "consumer" {
  source = "../../modules/container_app"

  depends_on = [
    azurerm_role_assignment.apps_kv_secrets_user,
    azurerm_key_vault_secret.rabbitmq_username,
    azurerm_key_vault_secret.rabbitmq_password,
  ]

  name                         = "${var.name_prefix}-consumer"
  resource_group_name          = azurerm_resource_group.main.name
  container_app_environment_id = azurerm_container_app_environment.main.id

  min_replicas = var.consumer_min_replicas
  max_replicas = var.consumer_max_replicas

  container_name = "consumer"
  image            = var.consumer_image
  cpu              = var.consumer_cpu
  memory           = var.consumer_memory

  environment_variables = merge(local.base_env, {
    CONSUMER_METRICS_PORT = "9100"
  })

  secret_environment_variables = {
    RABBITMQ_USERNAME = "rabbitmq-username"
    RABBITMQ_PASSWORD = "rabbitmq-password"
  }

  secrets = local.app_secrets

  ingress = null
}
