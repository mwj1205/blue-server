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

variable "aks_kubernetes_version" {
  description = "AKS Control Plane과 System Node Pool에 사용할 Kubernetes Version"
  type        = string
  default     = "1.36"

  validation {
    condition     = can(regex("^[0-9]+\\.[0-9]+(\\.[0-9]+)?$", var.aks_kubernetes_version))
    error_message = "aks_kubernetes_version은 1.36 또는 1.36.1과 같은 Kubernetes Version 형식이어야 합니다."
  }
}

variable "aks_system_node_vm_size" {
  description = "AKS System Node Pool에 사용할 Azure VM SKU"
  type        = string
  default     = "Standard_D4s_v5"

  validation {
    condition     = can(regex("^Standard_[A-Za-z0-9_]+$", var.aks_system_node_vm_size))
    error_message = "aks_system_node_vm_size는 Standard_D4s_v5와 같은 Azure VM SKU 형식이어야 합니다."
  }
}

variable "aks_system_node_count" {
  description = "학습용 AKS System Node Pool의 Node 수"
  type        = number
  default     = 2

  validation {
    condition = (
      floor(var.aks_system_node_count) == var.aks_system_node_count &&
      var.aks_system_node_count >= 2 &&
      var.aks_system_node_count <= 10
    )
    error_message = "aks_system_node_count는 AKS System Node Pool 제약을 만족하는 2~10 범위의 정수여야 합니다."
  }
}

variable "postgresql_version" {
  description = "Azure Database for PostgreSQL Flexible Server Major Version"
  type        = string
  default     = "18"

  validation {
    condition     = contains(["16", "17", "18"], var.postgresql_version)
    error_message = "postgresql_version은 지원되는 16, 17, 18 중 하나여야 합니다."
  }
}

variable "postgresql_zone" {
  description = "Azure Database for PostgreSQL Flexible Server Availability Zone"
  type        = string
  default     = "1"

  validation {
    condition     = contains(["1", "2", "3"], var.postgresql_zone)
    error_message = "postgresql_zone은 1, 2, 3 중 하나여야 합니다."
  }
}

variable "postgresql_sku_name" {
  description = "Azure Database for PostgreSQL Flexible Server에 사용할 개발용 SKU"
  type        = string
  default     = "B_Standard_B1ms"

  validation {
    condition     = can(regex("^(B|GP|MO)_Standard_[A-Za-z0-9_]+$", var.postgresql_sku_name))
    error_message = "postgresql_sku_name은 B_Standard_B1ms와 같은 Azure PostgreSQL SKU 형식이어야 합니다."
  }
}

variable "postgresql_storage_mb" {
  description = "Azure Database for PostgreSQL Flexible Server Storage 크기(MiB)"
  type        = number
  default     = 32768

  validation {
    condition     = contains([32768, 65536, 131072, 262144, 524288, 1048576], var.postgresql_storage_mb)
    error_message = "postgresql_storage_mb는 Azure PostgreSQL이 지원하는 32GiB 이상 Storage 크기여야 합니다."
  }
}

variable "postgresql_database_name" {
  description = "Blue Server Application Database 이름"
  type        = string
  default     = "bluearchive"

  validation {
    condition     = can(regex("^[A-Za-z][A-Za-z0-9_-]{0,62}$", var.postgresql_database_name))
    error_message = "postgresql_database_name은 영문자로 시작하는 1~63자의 영문자, 숫자, 밑줄, 하이픈이어야 합니다."
  }
}

variable "postgresql_administrator_login" {
  description = "Azure Database for PostgreSQL 관리자 Login 이름"
  type        = string
  default     = "blueadmin"

  validation {
    condition     = can(regex("^[A-Za-z][A-Za-z0-9_]{0,62}$", var.postgresql_administrator_login))
    error_message = "postgresql_administrator_login은 영문자로 시작하는 1~63자의 영문자, 숫자, 밑줄이어야 합니다."
  }
}

variable "postgresql_administrator_password" {
  description = "Azure Database for PostgreSQL 관리자 Password"
  type        = string
  sensitive   = true
  ephemeral   = true

  validation {
    condition = (
      length(var.postgresql_administrator_password) >= 8 &&
      length(var.postgresql_administrator_password) <= 128 &&
      (
        (length(regexall("[A-Z]", var.postgresql_administrator_password)) > 0 ? 1 : 0) +
        (length(regexall("[a-z]", var.postgresql_administrator_password)) > 0 ? 1 : 0) +
        (length(regexall("[0-9]", var.postgresql_administrator_password)) > 0 ? 1 : 0) +
        (length(regexall("[^A-Za-z0-9]", var.postgresql_administrator_password)) > 0 ? 1 : 0)
      ) >= 3
    )
    error_message = "postgresql_administrator_password는 8~128자이며 영문 대문자, 소문자, 숫자, 특수문자 중 3종 이상을 포함해야 합니다."
  }
}

variable "postgresql_administrator_password_version" {
  description = "Write-only PostgreSQL 관리자 Password 회전 Version"
  type        = number
  default     = 1

  validation {
    condition = (
      floor(var.postgresql_administrator_password_version) == var.postgresql_administrator_password_version &&
      var.postgresql_administrator_password_version >= 1
    )
    error_message = "postgresql_administrator_password_version은 1 이상의 정수여야 합니다."
  }
}

variable "managed_redis_sku_name" {
  description = "Azure Managed Redis에 사용할 개발용 SKU"
  type        = string
  default     = "Balanced_B0"

  validation {
    condition     = can(regex("^(Balanced_B|ComputeOptimized_X|MemoryOptimized_M|FlashOptimized_A)[0-9]+$", var.managed_redis_sku_name))
    error_message = "managed_redis_sku_name은 Balanced_B0과 같은 Azure Managed Redis SKU 형식이어야 합니다."
  }
}

variable "managed_redis_high_availability_enabled" {
  description = "Azure Managed Redis High Availability 활성화 여부"
  type        = bool
  default     = false
}

variable "managed_redis_public_network_access" {
  description = "Azure Managed Redis Public Endpoint 접근 설정"
  type        = string
  default     = "Enabled"

  validation {
    condition     = contains(["Enabled", "Disabled"], var.managed_redis_public_network_access)
    error_message = "managed_redis_public_network_access는 Enabled 또는 Disabled여야 합니다."
  }
}

variable "github_repository" {
  description = "Azure Federated Credential이 신뢰할 GitHub Repository의 owner/name"
  type        = string
  default     = "mwj1205/blue-server"

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$", var.github_repository))
    error_message = "github_repository는 owner/repository 형식이어야 합니다."
  }
}

variable "github_branch" {
  description = "Azure Federated Credential이 신뢰할 GitHub Branch"
  type        = string
  default     = "main"

  validation {
    condition     = length(trimspace(var.github_branch)) > 0
    error_message = "github_branch는 비어 있을 수 없습니다."
  }
}

variable "github_environment_name" {
  description = "Azure CD Federated Credential이 신뢰할 GitHub Environment 이름"
  type        = string
  default     = "azure-dev"

  validation {
    condition     = can(regex("^[A-Za-z0-9_.-]+$", var.github_environment_name))
    error_message = "github_environment_name은 영문, 숫자, 점, 밑줄, 하이픈만 사용할 수 있습니다."
  }
}

variable "tags" {
  description = "모든 Azure Resource에 추가할 사용자 정의 Tag"
  type        = map(string)
  default     = {}
}
