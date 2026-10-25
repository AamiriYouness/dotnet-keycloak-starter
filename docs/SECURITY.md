# Security design and deployment notes

## Trust boundaries

Keycloak is the trusted token issuer. The API accepts access tokens only after JWT signature, issuer, audience, and lifetime validation. It obtains discovery information and signing keys from the configured Keycloak endpoint. Possessing a syntactically valid JWT, or decoding its payload, is insufficient to authenticate.

The expected audience is `starter-api`. Role extraction occurs in the DI-registered `KeycloakJwtBearerEvents.TokenValidated` handler, after the token has passed validation. Both APIs share this handler; each uses its own audience. For API A, only roles under `resource_access.starter-api.roles` become API role claims. `RoleClaimType` selects an application-owned claim type rebuilt from those resource roles; incoming generic or application-role claims cannot bypass that mapping. The client ID in this path must continue to match the intended API if the audience is renamed.

In Client Credentials demonstrations, `sub` identifies a Keycloak service account; Web and exchange demonstrations preserve a human user's `sub`. A client credentials token does not represent a human login.

## Authentication and authorization

- 401 means the request has no acceptable access token.
- 403 means the token is accepted but the caller lacks the required role.
- `CanRead` requires an authenticated caller with `reader`.
- `CanAdmin` requires an authenticated caller with `admin`.
- Role names are case-sensitive. Admin does not automatically imply reader.
- The API's `admin` role grants no Keycloak administration privileges.
- Requests are evaluated using roles present in the token. Changing a role assignment does not rewrite an already issued JWT.

Access tokens are bearer credentials. Use HTTPS across untrusted networks, avoid logging tokens, and keep them out of URLs, source control, and screenshots. JWT validation is local: disabling a client or revoking a session does not by itself make an already issued JWT fail local validation immediately. Choose token lifetimes appropriate to the deployment and consider introspection for requirements that need immediate revocation checks.

The application uses the JWT handler's normal clock skew; tests set it to zero only to exercise short-lived token expiration promptly.

## Development configuration

Compose uses Keycloak `start-dev`, plain HTTP, a bootstrap admin account, and public sample credentials. It binds exposed ports to localhost. The API also runs in Development in this stack, exposing Swagger UI and its OpenAPI document and permitting HTTP discovery. Swagger accepts a pasted bearer token; it is not configured with a client secret.

Keycloak uses a dedicated PostgreSQL role that owns the Keycloak database and has no superuser privileges. PostgreSQL is not published on a host port. The initialization script uses psql's quoted variable interpolation, so password punctuation is handled safely.

The API runtime image uses .NET's non-root application user. JWT validation needs no client secret. API A uses a separate configured client credential for token exchange.

The Keycloak image is pinned to 26.8.1 and PostgreSQL to 17.11 in both Compose and integration tests. Keycloak 26.8.1 contains security fixes newer than the original 26.2.5 baseline; see [the official release notes](https://www.keycloak.org/2026/10/keycloak-2681-released). Keep patch versions under review and rerun the integration suite after changing them.

## Before deploying

1. Configure Keycloak for production using `start` with an optimized build, TLS or a correctly configured trusted TLS-terminating proxy, a fixed public hostname, and appropriate health/readiness probes. Change the demo realm's `sslRequired: none` to require TLS. See [Keycloak's production guide](https://www.keycloak.org/server/configuration-production).
2. Replace all demo identities, passwords, and client secrets. Store deployment credentials in a secret store and use separate credentials for each environment.
3. Run the API outside Development. Supply HTTPS `Keycloak__Authority` and `Keycloak__Audience`; supply a trusted HTTPS discovery URL when using `Keycloak__MetadataAddress`. Startup rejects HTTP authority or metadata URLs outside Development.
4. Configure reverse-proxy forwarding explicitly for your known proxy network and ensure HTTPS redirection receives the correct original scheme. Do not trust arbitrary forwarded headers.
5. Limit access to the Keycloak Admin Console, management endpoints, database, and infrastructure network. Apply least privilege to role assignments and scope mappings.
6. Back up PostgreSQL and rehearse migrations before upgrading Keycloak. Startup realm import skips existing realms; the checked-in JSON is a bootstrap template, not a migration system.
7. Set token lifetime, API rate limits, request size limits, audit retention, monitoring, and log redaction for the actual service.
8. The Razor Pages client uses authorization code with PKCE, exact redirect URIs, and server-side tickets. Preserve its key ring, encrypted tickets, and protecting certificate across deployments. For multiple web replicas, replace the single-instance file ticket store and refresh lock with a shared transactional store and distributed coordination. See [the web guide](WEB.md).
9. Define application-specific resource and ownership checks before adding business data. An API role alone does not prove ownership of an individual record.

## Verification

The container suite tests actual Keycloak-issued tokens against the API, including wrong audiences, foreign realms, expiration, tampering, malformed JWTs, and role isolation. The smoke script verifies that a newly imported development realm can exercise the full 200/401/403 flow.

The root CI workflow runs both checks on fresh infrastructure. See [the integration test guide](../tests/Keycloak.Starter.IntegrationTests/README.md).


## API-to-API exchange and worker boundaries

See [ARCHITECTURE.md](ARCHITECTURE.md) for the exact Keycloak 26.8.1 standard V2
configuration and permissions. The original user token targets API A only. A
authenticates itself to exchange it for a B-only access token, and B independently
checks audience, authorized party and its read role. Only A is enabled to request
exchange; scope mappings and user role assignments constrain the target. No
legacy exchange feature or legacy admin exchange permission is enabled.

The worker's client has only the A sync role, no exchange permission or browser
flow. Both client identity and scoped permission are checked. Token responses,
OAuth error descriptions and authorization headers must never be added to logs.
Configure HTTPS and external secret management before deploying; checked-in
credentials and plain HTTP backchannels are local development examples.


## Single-server reverse-proxy reference

[PRODUCTION.md](PRODUCTION.md) describes the separate production Compose file.
Forwarded headers are disabled by default; opting in requires exact trusted proxy
IPs. The middleware runs before HTTPS redirection/authentication. Public Web and
Keycloak routes are separated from the unpublished API proxy. Secrets are mounted
per service, and Production Web startup validates explicit storage and a usable
key-protection certificate. This is edge TLS on one trusted Docker host; the
private proxy-to-backend hops use HTTP. It is not a multi-host TLS or HA deployment.
