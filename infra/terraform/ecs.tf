resource "aws_ecs_cluster" "main" {
  name = local.name

  setting {
    name  = "containerInsights"
    value = "disabled"
  }
}

resource "aws_cloudwatch_log_group" "ecs" {
  name              = "/ecs/${local.name}"
  retention_in_days = 14
}

# ---------------------------------------------------------------- IAM

data "aws_iam_policy_document" "ecs_tasks_trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# Used by ECS itself: pull images, write logs, read the SSM secrets.
resource "aws_iam_role" "execution" {
  name               = "${local.name}-ecs-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_trust.json
}

resource "aws_iam_role_policy_attachment" "execution" {
  role       = aws_iam_role.execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

data "aws_iam_policy_document" "execution_secrets" {
  statement {
    actions   = ["ssm:GetParameters"]
    resources = [for parameter in aws_ssm_parameter.secrets : parameter.arn]
  }
}

resource "aws_iam_role_policy" "execution_secrets" {
  name   = "secrets"
  role   = aws_iam_role.execution.id
  policy = data.aws_iam_policy_document.execution_secrets.json
}

# Used by the containers. Only ECS Exec (`aws ecs execute-command`, for debugging) needs permissions.
resource "aws_iam_role" "task" {
  name               = "${local.name}-ecs-task"
  assume_role_policy = data.aws_iam_policy_document.ecs_tasks_trust.json
}

data "aws_iam_policy_document" "task_exec" {
  statement {
    actions = [
      "ssmmessages:CreateControlChannel",
      "ssmmessages:CreateDataChannel",
      "ssmmessages:OpenControlChannel",
      "ssmmessages:OpenDataChannel",
    ]
    resources = ["*"]
  }
}

resource "aws_iam_role_policy" "task_exec" {
  name   = "ecs-exec"
  role   = aws_iam_role.task.id
  policy = data.aws_iam_policy_document.task_exec.json
}

# ---------------------------------------------------------------- Task definitions

locals {
  image = { for name, repository in aws_ecr_repository.images : name => "${repository.repository_url}:latest" }

  secret_arn = { for key, parameter in aws_ssm_parameter.secrets : key => parameter.arn }

  log_configuration = {
    logDriver = "awslogs"
    options = {
      awslogs-group         = aws_cloudwatch_log_group.ecs.name
      awslogs-region        = var.region
      awslogs-stream-prefix = "ledger"
    }
  }

  # name => { cpu, memory, container definition }
  tasks = {
    api = {
      cpu    = 512
      memory = 1024
      container = {
        image        = local.image["api"]
        portMappings = [{ containerPort = 8080 }]
        environment = [
          { name = "ASPNETCORE_ENVIRONMENT", value = "Production" },
          { name = "Authentication__Authority", value = local.issuer },
          # Tokens keep the public issuer; signing keys are fetched through the internal address.
          { name = "Authentication__MetadataAddress", value = "http://${local.keycloak_host}:8080/realms/ledger/.well-known/openid-configuration" },
        ]
        secrets = [
          { name = "ConnectionStrings__Database", valueFrom = local.secret_arn["db/connection-string"] },
          { name = "ConnectionStrings__RabbitMq", valueFrom = local.secret_arn["rabbitmq/url"] },
        ]
      }
    }

    web = {
      cpu    = 256
      memory = 512
      container = {
        image        = local.image["web"]
        portMappings = [{ containerPort = 80 }]
        environment = [
          { name = "KEYCLOAK_ISSUER", value = local.issuer },
          # Never used: the load balancer sends /api and /health straight to the API.
          { name = "API_UPSTREAM", value = "http://127.0.0.1:8080" },
        ]
        secrets = []
      }
    }

    keycloak = {
      cpu    = 512
      memory = 1024
      container = {
        image        = local.image["keycloak"]
        portMappings = [{ containerPort = 8080 }, { containerPort = 9000 }]
        environment = [
          { name = "KC_HOSTNAME", value = "https://${local.auth_host}" },
          { name = "KC_DB_URL", value = "jdbc:postgresql://${aws_db_instance.main.address}:5432/keycloak?sslmode=require" },
          { name = "KC_DB_USERNAME", value = "ledger" },
          { name = "KC_BOOTSTRAP_ADMIN_USERNAME", value = "admin" },
          { name = "LEDGER_WEB_URL", value = local.app_url },
        ]
        secrets = [
          { name = "KC_DB_PASSWORD", valueFrom = local.secret_arn["db/password"] },
          { name = "KC_BOOTSTRAP_ADMIN_PASSWORD", valueFrom = local.secret_arn["keycloak/admin-password"] },
        ]
      }
    }

    rabbitmq = {
      cpu    = 256
      memory = 512
      container = {
        image        = "rabbitmq:4.2-alpine"
        portMappings = [{ containerPort = 5672 }]
        environment = [
          # Fixed node name: the data directory on EFS is named after it, so queues survive new tasks.
          { name = "RABBITMQ_NODENAME", value = "rabbit@localhost" },
          { name = "RABBITMQ_DEFAULT_USER", value = "ledger" },
        ]
        secrets = [
          { name = "RABBITMQ_DEFAULT_PASS", valueFrom = local.secret_arn["rabbitmq/password"] },
        ]
        mountPoints = [{ sourceVolume = "rabbitmq-data", containerPath = "/var/lib/rabbitmq" }]
      }
    }

    # One-off tasks run by the deploy workflow.
    migrator = {
      cpu    = 256
      memory = 512
      container = {
        image        = local.image["migrator"]
        portMappings = []
        environment  = []
        secrets = [
          { name = "ConnectionStrings__Database", valueFrom = local.secret_arn["db/connection-string"] },
        ]
      }
    }

    db-init = {
      cpu    = 256
      memory = 512
      container = {
        image        = "postgres:17-alpine"
        portMappings = []
        entryPoint   = ["sh", "-c"]
        command = [
          "psql -tAc \"SELECT 1 FROM pg_database WHERE datname = 'keycloak'\" | grep -q 1 || psql -c 'CREATE DATABASE keycloak'"
        ]
        environment = [
          { name = "PGHOST", value = aws_db_instance.main.address },
          { name = "PGUSER", value = "ledger" },
          { name = "PGDATABASE", value = "ledger" },
          { name = "PGSSLMODE", value = "require" },
        ]
        secrets = [
          { name = "PGPASSWORD", valueFrom = local.secret_arn["db/password"] },
        ]
      }
    }
  }
}

resource "aws_ecs_task_definition" "tasks" {
  for_each = local.tasks

  family                   = "${local.name}-${each.key}"
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = each.value.cpu
  memory                   = each.value.memory
  execution_role_arn       = aws_iam_role.execution.arn
  task_role_arn            = aws_iam_role.task.arn

  runtime_platform {
    operating_system_family = "LINUX"
    cpu_architecture        = "ARM64"
  }

  container_definitions = jsonencode([merge(each.value.container, {
    name             = each.key
    essential        = true
    logConfiguration = merge(local.log_configuration, { options = merge(local.log_configuration.options, { awslogs-stream-prefix = each.key }) })
  })])

  dynamic "volume" {
    for_each = each.key == "rabbitmq" ? [1] : []
    content {
      name = "rabbitmq-data"
      efs_volume_configuration {
        file_system_id     = aws_efs_file_system.rabbitmq.id
        transit_encryption = "ENABLED"
        authorization_config {
          access_point_id = aws_efs_access_point.rabbitmq.id
          iam             = "DISABLED"
        }
      }
    }
  }
}

# ---------------------------------------------------------------- Services

locals {
  services = {
    rabbitmq = { target_group = null, port = null, discovery = "rabbitmq", grace = null }
    keycloak = { target_group = aws_lb_target_group.keycloak.arn, port = 8080, discovery = "keycloak", grace = 300 }
    api      = { target_group = aws_lb_target_group.api.arn, port = 8080, discovery = null, grace = 120 }
    web      = { target_group = aws_lb_target_group.web.arn, port = 80, discovery = null, grace = 60 }
  }
}

resource "aws_ecs_service" "services" {
  for_each = local.services

  name                   = each.key
  cluster                = aws_ecs_cluster.main.id
  task_definition        = aws_ecs_task_definition.tasks[each.key].arn
  desired_count          = 1
  launch_type            = "FARGATE"
  enable_execute_command = true

  # RabbitMQ: never two brokers on the same data directory (stop the old task, then start the new one).
  deployment_minimum_healthy_percent = each.key == "rabbitmq" ? 0 : 100
  deployment_maximum_percent         = each.key == "rabbitmq" ? 100 : 200

  health_check_grace_period_seconds = each.value.grace

  network_configuration {
    subnets          = aws_subnet.public[*].id
    security_groups  = [aws_security_group.tasks.id]
    assign_public_ip = true # pulls images without a NAT gateway; the security group blocks inbound traffic
  }

  dynamic "load_balancer" {
    for_each = each.value.target_group == null ? [] : [1]
    content {
      target_group_arn = each.value.target_group
      container_name   = each.key
      container_port   = each.value.port
    }
  }

  dynamic "service_registries" {
    for_each = each.value.discovery == null ? [] : [1]
    content {
      registry_arn = aws_service_discovery_service.internal[each.value.discovery].arn
    }
  }

  depends_on = [aws_lb_listener.https, aws_efs_mount_target.rabbitmq]
}
