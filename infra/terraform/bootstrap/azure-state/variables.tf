variable "subscription_id" {
  description = "Terraform State Storage를 생성할 Azure Subscription ID"
  type        = string

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", var.subscription_id))
    error_message = "subscription_id는 유효한 UUID 형식이어야 합니다."
  }
}

variable "project_name" {
  description = "Backend Resource 이름과 Tag에 사용할 프로젝트 이름"
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

variable "location" {
  description = "Terraform State Storage를 생성할 Azure Region"
  type        = string
  default     = "koreacentral"

  validation {
    condition     = can(regex("^[a-z0-9]+$", var.location))
    error_message = "location은 koreacentral과 같은 Azure Region 식별자 형식이어야 합니다."
  }
}

variable "tags" {
  description = "Backend Resource에 추가할 사용자 정의 Tag"
  type        = map(string)
  default     = {}
}
