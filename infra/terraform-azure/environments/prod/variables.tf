variable "resource_group_name" {
  type        = string
  description = "Azure resource group name for this stack."
}

variable "location" {
  type        = string
  description = "Azure region (e.g. eastus)."
}

variable "name_prefix" {
  type        = string
  description = "Short prefix for resource names (e.g. poc-dev)."
}

variable "key_vault_name" {
  type        = string
  description = "Globally unique Key Vault name (3-24 letters, digits, hyphens)."
}

variable "log_retention_days" {
  type    = number
  default = 30
}

variable "purge_protection_enabled" {
  type        = bool
  default     = false
  description = "Enable purge protection on Key Vault (recommended for prod)."
}

variable "soft_delete_retention_days" {
  type    = number
  default = 7
}

variable "producer_image" {
  type = string
}

variable "consumer_image" {
  type = string
}

variable "producer_cpu" {
  type    = number
  default = 0.25
}

variable "producer_memory" {
  type    = string
  default = "0.5Gi"
}

variable "consumer_cpu" {
  type    = number
  default = 0.25
}

variable "consumer_memory" {
  type    = string
  default = "0.5Gi"
}

variable "producer_min_replicas" {
  type    = number
  default = 1
}

variable "producer_max_replicas" {
  type    = number
  default = 3
}

variable "consumer_min_replicas" {
  type    = number
  default = 1
}

variable "consumer_max_replicas" {
  type    = number
  default = 3
}

variable "rabbitmq_host" {
  type        = string
  description = "RabbitMQ hostname reachable from Container Apps (e.g. Azure VM, partner-hosted, or Amazon MQ endpoint)."
}

variable "rabbitmq_port" {
  type    = number
  default = 5671
}

variable "rabbitmq_username" {
  type      = string
  sensitive = true
}

variable "rabbitmq_password" {
  type      = string
  sensitive = true
}
