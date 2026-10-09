# Build context: clients/web
# Static files: build once on the build machine's architecture.
FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
# Builds every language (dist/web/browser/en and dist/web/browser/es).
RUN npx ng build

FROM nginx:1.29-alpine AS runtime
# The nginx image renders templates/*.template into conf.d/ with these variables at startup.
ENV API_UPSTREAM=http://api:8080 \
    KEYCLOAK_ISSUER=http://localhost:8080/realms/ledger
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
EXPOSE 80
