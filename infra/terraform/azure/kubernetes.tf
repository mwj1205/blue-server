# Helm Application을 배포할 학습용 AKS Cluster
resource "azurerm_kubernetes_cluster" "main" {
  name                = "aks-${local.name_prefix}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  dns_prefix          = "aks-${local.name_prefix}"

  kubernetes_version                = var.aks_kubernetes_version
  sku_tier                          = "Free"
  support_plan                      = "KubernetesOfficial"
  role_based_access_control_enabled = true
  local_account_disabled            = false

  # System Pod와 학습용 Application을 함께 실행할 최소 Node Pool
  default_node_pool {
    name                   = "system"
    node_count             = var.aks_system_node_count
    vm_size                = var.aks_system_node_vm_size
    os_sku                 = "AzureLinux3"
    os_disk_type           = "Managed"
    type                   = "VirtualMachineScaleSets"
    node_public_ip_enabled = false
    tags                   = local.common_tags

    # Azure 기본 Rolling Upgrade 설정과 Terraform State의 일치
    upgrade_settings {
      drain_timeout_in_minutes      = 0
      max_surge                     = "10%"
      node_soak_duration_in_minutes = 0
    }
  }

  # Client Secret을 저장하지 않는 AKS Control Plane Identity
  identity {
    type = "SystemAssigned"
  }

  # 별도 VNet 없이 Pod IP를 확장할 수 있는 Azure CNI Overlay
  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    load_balancer_sku   = "standard"
    outbound_type       = "loadBalancer"
  }

  tags = local.common_tags
}

# ACR Image Pull을 수행하는 AKS Kubelet Identity 권한
resource "azurerm_role_assignment" "aks_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_kubernetes_cluster.main.kubelet_identity[0].object_id
  principal_type       = "ServicePrincipal"
}
