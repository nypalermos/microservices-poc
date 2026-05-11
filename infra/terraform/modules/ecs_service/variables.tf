variable "name" {
  type        = string
  description = "Logical service name."
}

variable "environment" {
  type        = string
  description = "Deployment environment (dev/staging/prod)."
}

variable "container_image" {
  type        = string
  description = "Container image URI for the service."
}

variable "cpu" {
  type        = number
  description = "Task CPU units."
}

variable "memory" {
  type        = number
  description = "Task memory (MiB)."
}

variable "cluster_arn" {
  type        = string
  description = "ECS cluster ARN."
}

variable "subnet_ids" {
  type        = list(string)
  description = "Private subnet IDs for awsvpc networking."
}

variable "security_group_ids" {
  type        = list(string)
  description = "Security group IDs assigned to ECS tasks."
}

variable "execution_role_arn" {
  type        = string
  description = "Task execution IAM role ARN."
}

variable "task_role_arn" {
  type        = string
  description = "Task role IAM role ARN."
}

variable "container_port" {
  type        = number
  description = "Primary container port."
  default     = 8080
}

variable "desired_count" {
  type        = number
  description = "Desired number of running tasks."
  default     = 1
}

variable "environment_variables" {
  type        = map(string)
  description = "Plain environment variables for container runtime."
  default     = {}
}

variable "secrets" {
  type        = map(string)
  description = "Map of env var name to Secrets Manager/SSM ARN."
  default     = {}
}

variable "assign_public_ip" {
  type        = bool
  description = "Whether task ENI should have public IP."
  default     = false
}
