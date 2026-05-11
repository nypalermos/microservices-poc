variable "aws_region" {
  type = string
}

variable "ecs_cluster_arn" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "security_group_ids" {
  type = list(string)
}

variable "execution_role_arn" {
  type = string
}

variable "task_role_arn" {
  type = string
}

variable "rabbitmq_host" {
  type = string
}

variable "rabbitmq_port" {
  type    = string
  default = "5671"
}

variable "rabbitmq_username_secret_arn" {
  type = string
}

variable "rabbitmq_password_secret_arn" {
  type = string
}

variable "producer_image" {
  type = string
}

variable "consumer_image" {
  type = string
}
