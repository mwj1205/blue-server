# Azure 전역에서 중복되지 않는 Container Registry 이름 구성
locals {
  project_name_compact = replace(var.project_name, "-", "")
  subscription_hash    = substr(sha1(lower(var.subscription_id)), 0, 8)

  container_registry_name = "acr${substr(local.project_name_compact, 0, 20)}${var.environment}${local.subscription_hash}"
}

# Application Container Image를 저장할 Private Registry
resource "azurerm_container_registry" "main" {
  name                = local.container_registry_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = var.container_registry_sku

  admin_enabled                 = false
  anonymous_pull_enabled        = false
  public_network_access_enabled = true

  tags = local.common_tags
}
