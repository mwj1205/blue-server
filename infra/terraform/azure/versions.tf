# Terraform CLI와 AzureRM Provider 호환 범위 고정
terraform {
  required_version = "~> 1.15.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.79.0"
    }
  }
}
