# ---------------------------------------------------------------- PostgreSQL (databases "ledger" and "keycloak")

resource "aws_db_subnet_group" "main" {
  name       = local.name
  subnet_ids = aws_subnet.private[*].id
}

resource "random_password" "db" {
  length  = 32
  special = false
}

resource "aws_db_instance" "main" {
  identifier     = local.name
  engine         = "postgres"
  engine_version = "17"
  instance_class = var.db_instance_class

  allocated_storage     = 20
  max_allocated_storage = 50
  storage_type          = "gp3"
  storage_encrypted     = true

  # "ledger" is created with the instance; "keycloak" by the db-init task (see ecs.tf and the deploy workflow).
  db_name  = "ledger"
  username = "ledger"
  password = random_password.db.result

  db_subnet_group_name   = aws_db_subnet_group.main.name
  vpc_security_group_ids = [aws_security_group.db.id]
  publicly_accessible    = false
  multi_az               = false

  backup_retention_period    = 7
  backup_window              = "03:00-03:30"
  maintenance_window         = "sun:04:00-sun:05:00"
  auto_minor_version_upgrade = true

  deletion_protection       = true
  skip_final_snapshot       = false
  final_snapshot_identifier = "${local.name}-final"
}

# ---------------------------------------------------------------- RabbitMQ data (queued messages survive restarts)

resource "aws_efs_file_system" "rabbitmq" {
  encrypted        = true
  performance_mode = "generalPurpose"
  throughput_mode  = "bursting"
  tags             = { Name = "${local.name}-rabbitmq" }

  lifecycle_policy {
    transition_to_ia = "AFTER_30_DAYS"
  }
}

resource "aws_efs_mount_target" "rabbitmq" {
  count           = 2
  file_system_id  = aws_efs_file_system.rabbitmq.id
  subnet_id       = aws_subnet.public[count.index].id
  security_groups = [aws_security_group.efs.id]
}

# uid/gid 999 = the rabbitmq user of the official image.
resource "aws_efs_access_point" "rabbitmq" {
  file_system_id = aws_efs_file_system.rabbitmq.id

  posix_user {
    uid = 999
    gid = 999
  }

  root_directory {
    path = "/rabbitmq"
    creation_info {
      owner_uid   = 999
      owner_gid   = 999
      permissions = "0750"
    }
  }
}

# ---------------------------------------------------------------- Secrets (SSM Parameter Store, SecureString)

resource "random_password" "rabbitmq" {
  length  = 32
  special = false
}

resource "random_password" "keycloak_admin" {
  length  = 24
  special = false
}

locals {
  db_connection_string = join(";", [
    "Host=${aws_db_instance.main.address}",
    "Port=5432",
    "Database=ledger",
    "Username=ledger",
    "Password=${random_password.db.result}",
    "SSL Mode=Require",
    # db.t4g.micro allows ~80 connections: API 30 + Keycloak 15 + one-off tasks.
    "Maximum Pool Size=30",
  ])

  secrets = {
    "db/password"             = random_password.db.result
    "db/connection-string"    = local.db_connection_string
    "rabbitmq/password"       = random_password.rabbitmq.result
    "rabbitmq/url"            = "amqp://ledger:${random_password.rabbitmq.result}@${local.rabbitmq_host}:5672/"
    "keycloak/admin-password" = random_password.keycloak_admin.result
  }
}

resource "aws_ssm_parameter" "secrets" {
  for_each = local.secrets
  name     = "/${local.name}/${each.key}"
  type     = "SecureString"
  value    = each.value
}
