output "resource_group_id" {
  description = "생성된 Resource Group ID"
  value       = azurerm_resource_group.main.id
}

output "resource_group_name" {
  description = "생성된 Resource Group 이름"
  value       = azurerm_resource_group.main.name
}

output "resource_group_location" {
  description = "생성된 Resource Group Region"
  value       = azurerm_resource_group.main.location
}

output "container_registry_name" {
  description = "생성된 Azure Container Registry 이름"
  value       = azurerm_container_registry.main.name
}

output "container_registry_login_server" {
  description = "Docker Login과 Image 경로에 사용할 Azure Container Registry 주소"
  value       = azurerm_container_registry.main.login_server
}
