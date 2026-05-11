terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

locals {
  base_env = {
    EXCHANGE_NAME        = "poc.events"
    DLX_NAME             = "poc.dlx"
    QUEUE_NAME           = "poc.queue"
    RETRY_QUEUE_NAME     = "poc.queue.retry"
    DLQ_NAME             = "poc.queue.dlq"
    ROUTING_KEY          = "poc.message"
    RETRY_DELAY_MS       = "5000"
    MAX_RETRIES          = "5"
    RABBITMQ_HOST        = var.rabbitmq_host
    RABBITMQ_PORT        = var.rabbitmq_port
    RABBITMQ_SCHEME      = "amqps"
    RABBITMQ_TLS_ENABLED = "true"
    RABBITMQ_VHOST       = "/"
  }
}

module "producer_service" {
  source             = "../../modules/ecs_service"
  name               = "poc-producer-go-staging"
  environment        = "staging"
  container_image    = var.producer_image
  cpu                = 256
  memory             = 512
  cluster_arn        = var.ecs_cluster_arn
  subnet_ids         = var.private_subnet_ids
  security_group_ids = var.security_group_ids
  execution_role_arn = var.execution_role_arn
  task_role_arn      = var.task_role_arn
  container_port     = 8080
  desired_count      = 1
  environment_variables = merge(local.base_env, {
    PRODUCER_PORT = "8080"
  })
  secrets = {
    RABBITMQ_USERNAME = var.rabbitmq_username_secret_arn
    RABBITMQ_PASSWORD = var.rabbitmq_password_secret_arn
  }
}

module "consumer_service" {
  source             = "../../modules/ecs_service"
  name               = "poc-consumer-staging"
  environment        = "staging"
  container_image    = var.consumer_image
  cpu                = 256
  memory             = 512
  cluster_arn        = var.ecs_cluster_arn
  subnet_ids         = var.private_subnet_ids
  security_group_ids = var.security_group_ids
  execution_role_arn = var.execution_role_arn
  task_role_arn      = var.task_role_arn
  container_port     = 9100
  desired_count      = 1
  environment_variables = merge(local.base_env, {
    CONSUMER_METRICS_PORT = "9100"
  })
  secrets = {
    RABBITMQ_USERNAME = var.rabbitmq_username_secret_arn
    RABBITMQ_PASSWORD = var.rabbitmq_password_secret_arn
  }
}
