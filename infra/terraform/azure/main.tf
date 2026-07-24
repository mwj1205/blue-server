# 모든 Azure Resource에 적용할 공통 명명 규칙과 Tag
locals {
  name_prefix = "${var.project_name}-${var.environment}"

  common_tags = merge(
    var.tags,
    {
      project     = var.project_name
      environment = var.environment
      managed_by  = "terraform"
    }
  )
}

# Azure Resource의 논리적 관리 경계
resource "azurerm_resource_group" "main" {
  name     = "rg-${local.name_prefix}"
  location = var.location
  tags     = local.common_tags
}
