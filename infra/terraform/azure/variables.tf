variable "subscription_id" {
  description = "Terraform이 Resource를 관리할 Azure Subscription ID"
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.subscription_id))
    error_message = "subscription_id는 유효한 UUID 형식이어야 합니다."
  }
}

variable "project_name" {
  description = "Azure Resource 이름과 Tag에 사용할 프로젝트 이름"
  type        = string
  default     = "blue-server"

  validation {
    condition = (
      length(var.project_name) >= 2 &&
      length(var.project_name) <= 30 &&
      can(regex("^[a-z0-9][a-z0-9-]*[a-z0-9]$", var.project_name))
    )
    error_message = "project_name은 2~30자의 영문 소문자, 숫자, 하이픈만 사용할 수 있으며 하이픈으로 시작하거나 끝날 수 없습니다."
  }
}

variable "environment" {
  description = "배포 환경 식별자"
  type        = string
  default     = "dev"

  validation {
    condition     = contains(["dev", "stage", "prod"], var.environment)
    error_message = "environment는 dev, stage, prod 중 하나여야 합니다."
  }
}

variable "location" {
  description = "Azure Resource를 생성할 Region"
  type        = string
  default     = "koreacentral"

  validation {
    condition     = can(regex("^[a-z0-9]+$", var.location))
    error_message = "location은 koreacentral과 같은 Azure Region 식별자 형식이어야 합니다."
  }
}

variable "container_registry_sku" {
  description = "Azure Container Registry의 SKU"
  type        = string
  default     = "Basic"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.container_registry_sku)
    error_message = "container_registry_sku는 Basic, Standard, Premium 중 하나여야 합니다."
  }
}

variable "tags" {
  description = "모든 Azure Resource에 추가할 사용자 정의 Tag"
  type        = map(string)
  default     = {}
}
