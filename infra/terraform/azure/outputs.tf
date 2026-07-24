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
