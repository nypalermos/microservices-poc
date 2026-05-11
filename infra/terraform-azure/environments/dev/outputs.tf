output "resource_group_name" {
  value = azurerm_resource_group.main.name
}

output "container_app_environment_id" {
  value = azurerm_container_app_environment.main.id
}

output "key_vault_uri" {
  value       = azurerm_key_vault.main.vault_uri
  description = "Key Vault URI for secret management and rotation workflows."
}

output "producer_fqdn" {
  value       = module.producer.fqdn
  description = "Public URL host for the producer when ingress is enabled."
}

output "producer_publish_url" {
  value       = module.producer.fqdn != null ? "https://${module.producer.fqdn}/publish" : null
  description = "Example publish URL for smoke tests (adjust path if your API differs)."
}

output "managed_identity_principal_id" {
  value       = azurerm_user_assigned_identity.apps.principal_id
  description = "Principal id of the shared user-assigned identity used for Key Vault secret references."
}
