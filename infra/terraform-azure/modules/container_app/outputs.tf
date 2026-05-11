output "id" {
  value       = azurerm_container_app.this.id
  description = "Resource id of the Container App."
}

output "fqdn" {
  value       = try(azurerm_container_app.this.latest_revision_fqdn, null)
  description = "FQDN of the latest revision when ingress is enabled; null otherwise."
}

output "name" {
  value = azurerm_container_app.this.name
}
