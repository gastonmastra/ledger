# Production Keycloak (build context: infra/keycloak-prod).
# Optimized build for PostgreSQL; imports the "ledger" realm on the first start only (an existing realm is
# left as it is: change it in the admin console afterwards).
#
# Runtime environment (set by the ECS task):
#   KC_HOSTNAME=https://auth.<domain>     public URL: issuer of the tokens
#   KC_DB_URL / KC_DB_USERNAME / KC_DB_PASSWORD
#   KC_BOOTSTRAP_ADMIN_USERNAME / KC_BOOTSTRAP_ADMIN_PASSWORD (first start only)
#   LEDGER_WEB_URL=https://ledger.<domain> used by ledger-realm.json (redirect URIs of ledger-web)

ARG KEYCLOAK_VERSION=26.7.4

FROM --platform=$BUILDPLATFORM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION} AS builder
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:${KEYCLOAK_VERSION}
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY ledger-realm.json /opt/keycloak/data/import/ledger-realm.json
# HTTPS ends at the load balancer: plain HTTP inside, X-Forwarded-* headers trusted.
# The API validates tokens through the internal address (keycloak:8080) while the issuer stays public.
ENV KC_HTTP_ENABLED=true \
    KC_PROXY_HEADERS=xforwarded \
    KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true \
    KC_DB_POOL_MAX_SIZE=15
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
