# One load balancer for both hosts. HTTPS ends here; tasks get plain HTTP plus X-Forwarded-* headers.
#   ledger.<domain>  /api/*, /health -> api      everything else -> web (nginx, EN/ES builds)
#   auth.<domain>    admin console and master realm only from var.admin_cidrs, the rest -> keycloak

resource "aws_lb" "main" {
  name               = local.name
  load_balancer_type = "application"
  security_groups    = [aws_security_group.alb.id]
  subnets            = aws_subnet.public[*].id
}

resource "aws_lb_listener" "http" {
  load_balancer_arn = aws_lb.main.arn
  port              = 80
  protocol          = "HTTP"

  default_action {
    type = "redirect"
    redirect {
      port        = "443"
      protocol    = "HTTPS"
      status_code = "HTTP_301"
    }
  }
}

resource "aws_lb_listener" "https" {
  load_balancer_arn = aws_lb.main.arn
  port              = 443
  protocol          = "HTTPS"
  ssl_policy        = "ELBSecurityPolicy-TLS13-1-2-2021-06"
  certificate_arn   = aws_acm_certificate_validation.main.certificate_arn

  default_action {
    type = "fixed-response"
    fixed_response {
      content_type = "text/plain"
      message_body = "Not found"
      status_code  = "404"
    }
  }
}

# ---------------------------------------------------------------- Target groups

resource "aws_lb_target_group" "web" {
  name        = "${local.name}-web"
  port        = 80
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = aws_vpc.main.id

  deregistration_delay = 15

  health_check {
    path    = "/en/"
    matcher = "200"
  }
}

resource "aws_lb_target_group" "api" {
  name        = "${local.name}-api"
  port        = 8080
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = aws_vpc.main.id

  deregistration_delay = 30

  health_check {
    path    = "/health"
    matcher = "200"
  }
}

resource "aws_lb_target_group" "keycloak" {
  name        = "${local.name}-keycloak"
  port        = 8080
  protocol    = "HTTP"
  target_type = "ip"
  vpc_id      = aws_vpc.main.id

  deregistration_delay = 30

  # Keycloak serves health on its management port.
  health_check {
    path    = "/health/ready"
    port    = "9000"
    matcher = "200"
  }
}

# ---------------------------------------------------------------- Rules

locals {
  keycloak_admin_paths = ["/admin/*", "/realms/master/*"]
}

resource "aws_lb_listener_rule" "keycloak_admin_allowed" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 10

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.keycloak.arn
  }

  condition {
    host_header {
      values = [local.auth_host]
    }
  }
  condition {
    path_pattern {
      values = local.keycloak_admin_paths
    }
  }
  condition {
    source_ip {
      values = var.admin_cidrs
    }
  }
}

resource "aws_lb_listener_rule" "keycloak_admin_denied" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 20

  action {
    type = "fixed-response"
    fixed_response {
      content_type = "text/plain"
      message_body = "Forbidden"
      status_code  = "403"
    }
  }

  condition {
    host_header {
      values = [local.auth_host]
    }
  }
  condition {
    path_pattern {
      values = local.keycloak_admin_paths
    }
  }
}

resource "aws_lb_listener_rule" "keycloak" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 30

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.keycloak.arn
  }

  condition {
    host_header {
      values = [local.auth_host]
    }
  }
}

resource "aws_lb_listener_rule" "api" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 40

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.api.arn
  }

  condition {
    host_header {
      values = [local.app_host]
    }
  }
  condition {
    path_pattern {
      values = ["/api/*", "/health"]
    }
  }
}

resource "aws_lb_listener_rule" "web" {
  listener_arn = aws_lb_listener.https.arn
  priority     = 50

  action {
    type             = "forward"
    target_group_arn = aws_lb_target_group.web.arn
  }

  condition {
    host_header {
      values = [local.app_host]
    }
  }
}
