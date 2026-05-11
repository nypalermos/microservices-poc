variable "name" {
  type        = string
  description = "Container App name (unique within the environment)."
}

variable "resource_group_name" {
  type = string
}

variable "container_app_environment_id" {
  type = string
}

variable "revision_mode" {
  type        = string
  default     = "Single"
  description = "Single or Multiple revision mode."
}

variable "min_replicas" {
  type    = number
  default = 0
}

variable "max_replicas" {
  type    = number
  default = 3
}

variable "container_name" {
  type        = string
  description = "Primary container name inside the revision template."
}

variable "image" {
  type = string
}

variable "cpu" {
  type        = number
  description = "vCPU for the container (e.g. 0.25)."
}

variable "memory" {
  type        = string
  description = "Memory string (e.g. 0.5Gi)."
}

variable "environment_variables" {
  type        = map(string)
  default     = {}
  description = "Plain environment variables (non-secret)."
}

variable "secret_environment_variables" {
  type        = map(string)
  default     = {}
  description = "Map of env var name to Container App secret name (must exist in secrets map)."
}

variable "secrets" {
  type = map(object({
    key_vault_secret_id = string
    identity_id         = string
  }))
  default     = {}
  description = "Container App secret name -> Key Vault secret reference and user-assigned identity id."
}

variable "ingress" {
  type = object({
    external_enabled = bool
    target_port      = number
    transport        = optional(string, "auto")
  })
  default = null
  description = "If null, no HTTP ingress is configured (typical for workers)."
}
