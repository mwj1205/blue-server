# GitHub Actions의 Client Secret 없는 Azure 인증용 Identity
resource "azurerm_user_assigned_identity" "github_actions" {
  name                = "id-${local.name_prefix}-github-actions"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  tags                = local.common_tags
}

# main Branch에서 발급된 GitHub OIDC Token만 신뢰하는 Federated Credential
resource "azurerm_federated_identity_credential" "github_actions_main" {
  name                      = "github-main"
  audience                  = ["api://AzureADTokenExchange"]
  issuer                    = "https://token.actions.githubusercontent.com"
  user_assigned_identity_id = azurerm_user_assigned_identity.github_actions.id
  subject                   = "repo:${var.github_repository}:ref:refs/heads/${var.github_branch}"
}

# OIDC 인증과 Resource 조회 검증을 위한 최소 권한
resource "azurerm_role_assignment" "github_actions_resource_group_reader" {
  scope                = azurerm_resource_group.main.id
  role_definition_name = "Reader"
  principal_id         = azurerm_user_assigned_identity.github_actions.principal_id
  principal_type       = "ServicePrincipal"
}
