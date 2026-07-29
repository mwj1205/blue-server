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

output "github_actions_identity_name" {
  description = "GitHub Actions OIDC 인증에 사용할 User Assigned Managed Identity 이름"
  value       = azurerm_user_assigned_identity.github_actions.name
}

output "github_actions_client_id" {
  description = "GitHub Actions의 AZURE_CLIENT_ID에 등록할 Managed Identity Client ID"
  value       = azurerm_user_assigned_identity.github_actions.client_id
}

output "github_actions_tenant_id" {
  description = "GitHub Actions의 AZURE_TENANT_ID에 등록할 Tenant ID"
  value       = azurerm_user_assigned_identity.github_actions.tenant_id
}
