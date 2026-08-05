# Backend Resource를 생성할 Azure Subscription 명시
provider "azurerm" {
  subscription_id                 = var.subscription_id
  resource_provider_registrations = "none"

  features {}
}
