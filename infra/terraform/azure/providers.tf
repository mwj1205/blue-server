# AzureRM 4.x에서 Plan 대상 Subscription 명시
provider "azurerm" {
  subscription_id = var.subscription_id

  features {}
}
