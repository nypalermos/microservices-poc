output "service_name" {
  value       = aws_ecs_service.service.name
  description = "Service logical name."
}

output "task_definition_arn" {
  value       = aws_ecs_task_definition.service.arn
  description = "Task definition ARN."
}
