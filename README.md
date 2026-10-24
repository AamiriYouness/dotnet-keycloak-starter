# Keycloak + ASP.NET Core Security Starter

A portfolio starter demonstrating JWT authentication, API-specific client roles, authorization policies, and integration tests against real Keycloak and PostgreSQL containers.

The API is stateless. PostgreSQL stores Keycloak's configuration and identities. Demo callers use OAuth 2.0 client credentials to authenticate as service accounts.

~~~mermaid
flowchart LR
    Client[Demo client] -->|Request access token| Keycloak
    Keycloak --> PostgreSQL
    Client -->|Bearer access token| API[ASP.NET Core API]
    API -->|Discovery and signing keys| Keycloak
~~~

## Quick start: complete Docker stack

Requirements: Docker with Compose v2 and Python 3 for the smoke checks. The first run downloads and builds the images.

~~~sh
docker compose --profile api up --build -d --wait --wait-timeout 180
python3 scripts/smoke-test.py
~~~

The smoke script checks all endpoints with anonymous, reader, admin, and unprivileged callers. It prints HTTP statuses without printing tokens or secrets.

| Service | Address |
| --- | --- |
| API | http://localhost:5150 |
| Keycloak Admin Console | http://localhost:8080/admin/ |
| Development Swagger UI | http://localhost:5150/swagger/ |
| Development OpenAPI document | http://localhost:5150/openapi/v1.json |

The default local admin login is `admin` / `local-admin-password`. All supplied passwords and client secrets are public development examples. Override local server credentials by copying `.env.example` to `.env` before the first startup. Keep real secrets out of source control.

### Existing development data from an earlier checkout

Startup import skips realms that already exist. Updating `realm-export.json` does not modify the realm in an existing PostgreSQL volume. The Keycloak image has also been updated; back up an existing database before upgrading it.

To try the finished demo alongside an existing setup, use a separate project and ports:

~~~sh
KEYCLOAK_PORT=8082 API_PORT=5152 docker compose -p starter-demo --profile api up --build -d --wait --wait-timeout 180
KEYCLOAK_URL=http://localhost:8082 API_URL=http://localhost:5152 python3 scripts/smoke-test.py
~~~

### Run the API on your host

Install a stable .NET 10 SDK, then start the dependencies and the API:

~~~sh
docker compose up -d --wait --wait-timeout 180
dotnet run --project src/Keycloak.Starter.Api --launch-profile http
~~~

This starts the API at http://localhost:5150. The HTTP launch profile is for local development. For a custom Keycloak port, set `Keycloak__Authority` to the matching realm URL when starting the API.

## Endpoints and permissions

| Endpoint | Required permission | Expected response |
| --- | --- | --- |
| `GET /health` | Public | 200, `Ok!` |
| `GET /me` | Any valid API access token | 200 with the token's `sub` |
| `GET /reader` | `starter-api` client role `reader` | 200, otherwise 403 |
| `GET /admin` | `starter-api` client role `admin` | 200, otherwise 403 |

A missing or invalid token produces **401** with a Bearer challenge. A valid token without the endpoint's required role produces **403**. The `admin` role is independent of `reader`; assign both when a caller needs both permissions.

## Reproducible demo identities

The development realm is defined in [keycloak/realm-export.json](keycloak/realm-export.json). A fresh database imports:

| Client ID | Purpose / role | Local demo secret |
| --- | --- | --- |
| `starter-api` | Resource server and expected audience | No secret used by the API |
| `starter-cli` | Service account with `reader` | `local-demo-reader-secret` |
| `starter-admin` | Service account with `admin` | `local-demo-admin-secret` |
| `starter-no-role` | Authenticated caller with no API role | `local-demo-none-secret` |

Interactive login and password grants are disabled on the demo clients. Role scope mappings restrict each client to its assigned API roles, and an audience mapper adds `starter-api` to access tokens.

To request a reader token manually, use Bash or zsh:

~~~sh
TOKEN=$(curl --fail --silent --show-error \
  http://localhost:8080/realms/starter/protocol/openid-connect/token \
  --data-urlencode grant_type=client_credentials \
  --data-urlencode client_id=starter-cli \
  --data-urlencode client_secret=local-demo-reader-secret \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')

curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5150/reader
curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5150/admin
unset TOKEN
~~~

The first API request returns 200; the second returns 403.

## Try authenticated requests in Swagger UI

Open http://localhost:5150/swagger/ (or http://localhost:5152/swagger/ for the separately named demo).

1. Request an access token using the assignment to `TOKEN` above. Before clearing it, run `printf '%s\n' "$TOKEN"` locally and copy the token.
2. Click **Authorize**, paste the raw access token without a `Bearer ` prefix, and authorize. Swagger UI adds the header automatically.
3. Expand `/reader`, select **Try it out**, and **Execute**. A reader token returns 200.
4. Try `/admin` with the same token to see the expected 403. Use a token from `starter-admin` for a successful admin request.
5. Use **Authorize → Logout** to clear the token when finished.

The UI and document are available only in Development. Protected operations declare the Bearer requirement and their authentication/authorization failure responses; `/health` remains public. The UI accepts access tokens and requires no client secret.

## How token validation works

ASP.NET Core validates the access token's signature, issuer, audience, and lifetime using Keycloak discovery and its published signing keys. The API needs no client secret to validate JWTs.

After validation, the API maps only `resource_access.starter-api.roles` into .NET role claims. Realm roles and roles belonging to other clients grant no API permission. Named policies `CanRead` and `CanAdmin` enforce the corresponding roles. Missing or malformed role data grants no roles.

The Docker API uses `Keycloak__MetadataAddress` to reach discovery over the internal Compose network. Keycloak advertises the canonical public issuer while resolving backchannel endpoints internally, so a token requested from localhost can be verified by the API container. Issuer validation remains enabled.

## Tests

Requirements: .NET 10 and a running Docker engine.

~~~sh
dotnet test Keycloak.Starter.sln
~~~

Testcontainers starts disposable Keycloak and PostgreSQL instances, imports isolated test realms, and cleans up afterward. The suite tests anonymous access, successful role checks, forbidden requests, subject propagation, foreign-role isolation, and rejection of invalid tokens. It uses the real JWT bearer handler.

See the [test guide](tests/Keycloak.Starter.IntegrationTests/README.md) for the full matrix and fast-test command. The expiration test uses zero clock skew to avoid waiting for the application's normal grace period.

## Configuration

| Setting | Meaning |
| --- | --- |
| `Keycloak__Authority` | Realm issuer URL; required, HTTPS outside Development |
| `Keycloak__Audience` | Expected API audience; required |
| `Keycloak__MetadataAddress` | Optional discovery URL for an internal network |
| `KEYCLOAK_PORT` | Compose's localhost Keycloak port; default 8080 |
| `API_PORT` | Compose's localhost API port; default 5150 |

Host development defaults are in `appsettings.Development.json`. Compose sets these values for the API container. PostgreSQL is available only inside the Compose network, and published API/Keycloak ports bind to localhost.

For deployment, provide HTTPS authority and discovery URLs through deployment configuration, rotate credentials, and use a properly configured Keycloak production server. Read [the security and deployment notes](docs/SECURITY.md).

## CI

[GitHub Actions](.github/workflows/ci.yml) restores packages, builds Release with warnings as errors, runs the Testcontainers suite, builds a fresh Docker stack, and runs the development smoke checks. It requires no repository secrets.

## Stop, reset, and troubleshoot

~~~sh
docker compose --profile api down
docker compose --profile api logs --tail=100 keycloak api
~~~

`down` preserves PostgreSQL data. **`docker compose --profile api down --volumes` deletes this project's database, realm changes, and accounts.** Use that only for disposable data. For a named project, include the same `-p` option when stopping or resetting it.

Common issues:

- **401 with a token:** check the issuer, expiry, and `aud`. Use the configured hostname consistently. Never fix this by disabling validation.
- **403:** check the service account's role assignment, the client's role scope mappings, and `resource_access.starter-api.roles` in a fresh token.
- **Demo token request fails after updating the repository:** an older realm may exist in the volume. Use the separate-project example above or deliberately migrate the realm.
- **Docker test startup fails:** start Docker Desktop, check image registry access, and retry. The tests report container startup logs when initialization fails.
- **Port already in use:** set `API_PORT` and `KEYCLOAK_PORT` before startup.
- **Database password changed in .env:** PostgreSQL initialization scripts run only on an empty volume. Changing environment variables does not update existing database-role passwords.

## Project map

- `src/Keycloak.Starter.Api/Program.cs` — explicit authentication, role mapping, policies, and endpoints.
- `keycloak/realm-export.json` — reproducible development realm.
- `db/init/01-create-databases.sh` — database owner creation with safe password quoting.
- `tests/Keycloak.Starter.IntegrationTests` — real-token security integration tests.
- `scripts/smoke-test.py` — end-to-end verification of the development stack.
- `docs/SECURITY.md` — trust boundaries and deployment guidance.
