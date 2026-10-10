# The deploy workflow pushes <commit sha> and "latest"; the task definitions use "latest" and the workflow
# forces a new deployment, so Terraform and deploys never fight over the image.

locals {
  images = ["api", "migrator", "web", "keycloak"]
}

resource "aws_ecr_repository" "images" {
  for_each             = toset(local.images)
  name                 = "${local.name}/${each.value}"
  image_tag_mutability = "MUTABLE"
  force_delete         = false

  image_scanning_configuration {
    scan_on_push = true
  }
}

resource "aws_ecr_lifecycle_policy" "images" {
  for_each   = aws_ecr_repository.images
  repository = each.value.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Keep the last 10 images"
      selection = {
        tagStatus   = "any"
        countType   = "imageCountMoreThan"
        countNumber = 10
      }
      action = { type = "expire" }
    }]
  })
}
