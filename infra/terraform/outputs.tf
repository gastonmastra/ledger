output "app_url" {
  value = local.app_url
}

output "keycloak_admin_url" {
  value = "https://${local.auth_host}/admin/"
}

output "keycloak_admin_password_parameter" {
  description = "aws ssm get-parameter --with-decryption --name <this> --query Parameter.Value --output text"
  value       = aws_ssm_parameter.secrets["keycloak/admin-password"].name
}

output "ecr_repositories" {
  value = { for name, repository in aws_ecr_repository.images : name => repository.repository_url }
}

output "db_endpoint" {
  value = aws_db_instance.main.address
}
