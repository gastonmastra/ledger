locals {
  name       = "ledger"
  app_host   = "ledger.${var.domain}"
  auth_host  = "auth.${var.domain}"
  app_url    = "https://${local.app_host}"
  issuer     = "https://${local.auth_host}/realms/ledger"
  account_id = data.aws_caller_identity.current.account_id

  # Internal names (Cloud Map private DNS) used between tasks.
  internal_domain = "ledger.internal"
  rabbitmq_host   = "rabbitmq.${local.internal_domain}"
  keycloak_host   = "keycloak.${local.internal_domain}"
}

data "aws_caller_identity" "current" {}

data "aws_availability_zones" "available" {
  state = "available"
}
