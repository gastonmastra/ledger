variable "region" {
  type    = string
  default = "eu-west-1"
}

variable "domain" {
  description = "Domain registered in Route 53 (its hosted zone must exist). The app is served at ledger.<domain>, Keycloak at auth.<domain>."
  type        = string
}

variable "admin_cidrs" {
  description = "Networks allowed to open the Keycloak admin console (e.g. [\"203.0.113.7/32\"])."
  type        = list(string)
}

variable "alert_email" {
  description = "Receives the monthly budget alerts."
  type        = string
}

variable "monthly_budget_usd" {
  type    = number
  default = 120
}

variable "db_instance_class" {
  type    = string
  default = "db.t4g.micro"
}
