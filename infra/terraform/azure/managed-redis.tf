# Subscription마다 재현 가능한 전역 고유 Managed Redis 이름
locals {
  managed_redis_name = "redis-${local.name_prefix}-${local.subscription_hash}"
}

# Application Cache와 Orleans membership을 Pod 수명에서 분리한 Managed Redis
resource "azurerm_managed_redis" "main" {
  name                = local.managed_redis_name
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  sku_name                  = var.managed_redis_sku_name
  high_availability_enabled = var.managed_redis_high_availability_enabled
  public_network_access     = var.managed_redis_public_network_access

  default_database {
    # Workload Identity 전환 전 Kubernetes Secret 주입을 위한 Access Key 인증
    access_keys_authentication_enabled = true

    # Public Endpoint에서도 평문 전송을 허용하지 않는 TLS 연결
    client_protocol = "Encrypted"

    # 기존 단일 Redis 동작과 Orleans membership 명령 호환성 유지
    clustering_policy = "NoCluster"

    # Membership Key의 비의도적 제거보다 명시적인 Write 실패를 선택
    eviction_policy = "NoEviction"
  }

  tags = local.common_tags
}
