# Subscription마다 재현 가능한 전역 고유 PostgreSQL Server 이름
locals {
  postgresql_server_name      = "psql-${local.name_prefix}-${local.subscription_hash}"
  aks_outbound_public_ip_id   = one(azurerm_kubernetes_cluster.main.network_profile[0].load_balancer_profile[0].effective_outbound_ips)
  aks_outbound_public_ip_name = basename(local.aks_outbound_public_ip_id)
}

# AKS Managed Load Balancer가 생성한 단일 Outbound Public IP 조회
data "azurerm_public_ip" "aks_outbound" {
  name                = local.aks_outbound_public_ip_name
  resource_group_name = azurerm_kubernetes_cluster.main.node_resource_group
}

# AKS Pod 수명과 분리된 학습용 Managed PostgreSQL Server
resource "azurerm_postgresql_flexible_server" "main" {
  name                = local.postgresql_server_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  version                           = var.postgresql_version
  administrator_login               = var.postgresql_administrator_login
  administrator_password_wo         = var.postgresql_administrator_password
  administrator_password_wo_version = var.postgresql_administrator_password_version

  sku_name                      = var.postgresql_sku_name
  storage_mb                    = var.postgresql_storage_mb
  storage_tier                  = "P4"
  auto_grow_enabled             = false
  backup_retention_days         = 7
  geo_redundant_backup_enabled  = false
  public_network_access_enabled = true

  # Application 연결에 필요한 Password 인증만 활성화
  authentication {
    active_directory_auth_enabled = false
    password_auth_enabled         = true
  }

  tags = local.common_tags
}

# EF Core Migration을 적용할 Application Database
resource "azurerm_postgresql_flexible_server_database" "main" {
  name      = var.postgresql_database_name
  server_id = azurerm_postgresql_flexible_server.main.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}

# 모든 Azure Service가 아닌 현재 AKS Outbound IP만 허용
resource "azurerm_postgresql_flexible_server_firewall_rule" "aks" {
  name             = "AllowAksOutbound"
  server_id        = azurerm_postgresql_flexible_server.main.id
  start_ip_address = data.azurerm_public_ip.aks_outbound.ip_address
  end_ip_address   = data.azurerm_public_ip.aks_outbound.ip_address
}
