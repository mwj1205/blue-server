output "resource_group_name" {
  description = "Terraform State Storage가 속한 Resource Group 이름"
  value       = azurerm_resource_group.terraform_state.name
}

output "storage_account_name" {
  description = "Terraform State를 저장할 Storage Account 이름"
  value       = azurerm_storage_account.terraform_state.name
}

output "container_name" {
  description = "Terraform State Blob Container 이름"
  value       = azurerm_storage_container.terraform_state.name
}
