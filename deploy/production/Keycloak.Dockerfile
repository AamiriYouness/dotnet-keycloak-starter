FROM quay.io/keycloak/keycloak:26.8.1 AS build
ENV KC_DB=postgres KC_HEALTH_ENABLED=true
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.8.1
COPY --from=build /opt/keycloak/ /opt/keycloak/
COPY deploy/production/keycloak-entrypoint.sh /opt/keycloak/bin/starter-entrypoint.sh
ENTRYPOINT ["/bin/bash", "/opt/keycloak/bin/starter-entrypoint.sh"]
