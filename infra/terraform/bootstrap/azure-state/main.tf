# 현재 Azure CLI Principal에 State Blob 접근 권한 부여
data "azurerm_client_config" "current" {}

# Subscription별로 재현 가능한 전역 고유 Storage Account 이름 구성
locals {
  project_name_compact = replace(var.project_name, "-", "")
  subscription_hash    = substr(sha1(lower(var.subscription_id)), 0, 8)

  storage_account_name = "st${substr(local.project_name_compact, 0, 10)}tf${local.subscription_hash}"

  common_tags = merge(
    var.tags,
    {
      project    = var.project_name
      managed_by = "terraform"
      purpose    = "terraform-state"
    }
  )
}

# Application Resource와 수명을 분리한 State 전용 Resource Group
resource "azurerm_resource_group" "terraform_state" {
  name     = "rg-${var.project_name}-tfstate"
  location = var.location
  tags     = local.common_tags
}

# 로컬 개발과 GitHub Hosted Runner 접근을 위한 State Storage
resource "azurerm_storage_account" "terraform_state" {
  name                = local.storage_account_name
  resource_group_name = azurerm_resource_group.terraform_state.name
  location            = azurerm_resource_group.terraform_state.location

  account_kind             = "StorageV2"
  account_tier             = "Standard"
  account_replication_type = "LRS"

  min_tls_version                  = "TLS1_2"
  https_traffic_only_enabled       = true
  public_network_access_enabled    = true
  allow_nested_items_to_be_public  = false
  default_to_oauth_authentication  = true
  shared_access_key_enabled        = true
  local_user_enabled               = false
  cross_tenant_replication_enabled = false

  blob_properties {
    versioning_enabled = true

    delete_retention_policy {
      days = 7
    }

    container_delete_retention_policy {
      days = 7
    }
  }

  tags = local.common_tags
}

# 외부 공개를 허용하지 않는 Terraform State Container
resource "azurerm_storage_container" "terraform_state" {
  name                  = "tfstate"
  storage_account_id    = azurerm_storage_account.terraform_state.id
  container_access_type = "private"
}

# Access Key 대신 Microsoft Entra ID로 State Blob에 접근하기 위한 권한
resource "azurerm_role_assignment" "terraform_state_blob_data_contributor" {
  scope                = azurerm_storage_account.terraform_state.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = data.azurerm_client_config.current.object_id
}
