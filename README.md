# Keycloak + ASP.NET Core Security Starter

A portfolio starter demonstrating JWT authentication, API-specific client roles, authorization policies, and integration tests against real Keycloak and PostgreSQL containers.

The existing API is API A (Accounts). A second Transactions API demonstrates audience-restricted user access through RFC 8693 token exchange. An independent .NET background worker uses Client Credentials for a synchronization check. Both APIs are stateless; PostgreSQL stores Keycloak's configuration and identities.
The Razor Pages web application also supports browser login through authorization code with PKCE, server-side token storage, and sessions that survive container recreation. See [the web setup and Playwright guide](docs/WEB.md) for the complete browser demo.
The web guide includes Bash and PowerShell setup, explains the cross-platform PFX certificate format, and shows how the host preparation step differs from Docker image build and container initialization.

~~~mermaid
flowchart LR
    Browser -->|Secure session cookie| Web[Razor Pages]
    Web -->|User token: aud starter-api| A[API A / Accounts]
    A -->|RFC 8693 exchange| KC[Keycloak 26.8.1]
    KC -->|Token: aud starter-transactions| A
    A -->|Exchanged user token| B[API B / Transactions]
    Worker -->|Client Credentials| KC
    Worker -->|Service token: sync role| A
    KC --> PostgreSQL
~~~

For a separate single-server deployment with HTTPS proxies, production Keycloak,
file-mounted secrets and a backup/restore runbook, see [the production reference](docs/PRODUCTION.md).
The default Compose file below remains the development demonstration.

## Quick start: complete Docker stack

Requirements: Docker with Compose v2, .NET 10 (for certificate preparation and tests), and Python 3 for smoke checks. The first run downloads and builds images. On this workstation use Docker Desktop:

~~~sh
export DOCKER_CONTEXT=desktop-linux
export DOCKER_HOST=unix://$HOME/.docker/run/docker.sock
~~~

These exports apply to Docker CLI/Compose and Testcontainers respectively. Other platforms can use their normal Docker engine configuration. Use a fresh Compose project/database to import the new realm; existing realms are not overwritten.

~~~sh
bash scripts/prepare-web.sh
docker compose up --build -d --wait --wait-timeout 180
python3 scripts/smoke-test.py
~~~

The smoke script checks the original API endpoints with anonymous, reader, admin, and unprivileged callers. It prints HTTP statuses without printing tokens or secrets.

| Service | Address |
| --- | --- |
| Web | https://localhost:7002 |
| API A / Accounts | http://localhost:5150 |
| API B / Transactions | http://localhost:5160 |
| Keycloak Admin Console | http://localhost:8080/admin/ |
| Development Swagger UI | http://localhost:5150/swagger/ |
| Development OpenAPI document | http://localhost:5150/openapi/v1.json |

The default local admin login is `admin` / `local-admin-password`. All supplied passwords and client secrets are public development examples. Override local server credentials by copying `.env.example` to `.env` before the first startup. Keep real secrets out of source control.

### Existing development data from an earlier checkout

Startup import skips realms that already exist. Updating `realm-export.json` does not modify the realm in an existing PostgreSQL volume. The Keycloak image has also been updated; back up an existing database before upgrading it.

To try the finished demo alongside an existing setup, use a separate project and ports:

~~~sh
KEYCLOAK_PORT=8082 API_PORT=5152 TRANSACTIONS_PORT=5162 docker compose -p starter-delegation up postgres keycloak api transactions worker --build -d --wait --wait-timeout 180
KEYCLOAK_URL=http://localhost:8082 API_URL=http://localhost:5152 python3 scripts/smoke-test.py
~~~

### Run the API on your host

Install a stable .NET 10 SDK, then start the dependencies and the API:

~~~sh
docker compose up postgres keycloak -d --wait --wait-timeout 180
dotnet run --project src/Keycloak.Starter.Api --launch-profile http
~~~

This starts the API at http://localhost:5150. The HTTP launch profile is for local development. For a custom Keycloak port, set `Keycloak__Authority` to the matching realm URL when starting the API.

## Endpoints and permissions

| Endpoint | Required permission | Expected response |
| --- | --- | --- |
| `GET /health` | Public | 200, `Ok!` |
| `GET /me` | Any valid API access token | 200 with the token's `sub` |
| `GET /reader` | `starter-api` client role `reader` | 200, otherwise 403 |
| `GET /accounts/transactions` | Web user with A `reader`; exchange also requires user B `read-transactions` | 200 with B response |
| `GET /sync/status` | `starter-worker` client + A `sync` role | 200; users receive 403 |
| API B: `GET /transactions` | B audience + `starter-api` authorized party + B `read-transactions` | 200; A-only token receives 401 |
| `GET /admin` | `starter-api` client role `admin` | 200, otherwise 403 |

A missing or invalid token produces **401** with a Bearer challenge. A valid token without the endpoint's required role produces **403**. The `admin` role is independent of `reader`; assign both when a caller needs both permissions.

## Reproducible demo identities

The development realm is defined in [keycloak/realm-export.json](keycloak/realm-export.json). A fresh database imports:

| Client ID | Purpose / role | Local demo secret |
| --- | --- | --- |
| `starter-api` | API A resource and confidential exchange requester | `local-demo-exchange-secret` (exchange only) |
| `starter-transactions` | API B resource, `read-transactions` | None |
| `starter-web` | Browser users, authorization code + PKCE | `local-demo-web-secret` |
| `starter-worker` | Service account, A `sync` only | `local-demo-worker-secret` |
| `starter-cli` | Historical curl/smoke reader account; not a CLI package | `local-demo-reader-secret` |
| `starter-admin` | Service account with `admin` | `local-demo-admin-secret` |
| `starter-no-role` | Authenticated caller with no API role | `local-demo-none-secret` |

Password grants are disabled on every imported client. Interactive login is enabled only for `starter-web`. Role scope mappings restrict each client to its assigned API roles, and an audience mapper adds `starter-api` to access tokens.

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

ASP.NET Core validates the access token's signature, issuer, audience, and lifetime using Keycloak discovery and its published signing keys. Neither API needs a client secret to validate JWTs. API A separately uses its client secret to authenticate token-exchange requests.

After validation, the shared `KeycloakJwtBearerEvents` handler maps only each API's own `resource_access.<audience>.roles` into application-owned .NET role claims. Both APIs use `RequireRole` policies; `RoleClaimType` selects those mapped claims. Realm roles and roles belonging to other clients grant no API permission. Named policies `CanRead` and `CanAdmin` enforce the corresponding roles. Missing or malformed role data grants no roles.

The Docker API uses `Keycloak__MetadataAddress` to reach discovery over the internal Compose network. Keycloak advertises the canonical public issuer while resolving backchannel endpoints internally, so a token requested from localhost can be verified by the API container. Issuer validation remains enabled.

## Tests

Requirements: .NET 10, a running Docker engine, and Playwright Chromium for the browser suite. Follow the one-time browser installation instructions in [the web guide](docs/WEB.md) before running the full solution. The API integration project can also be run independently.

~~~sh
dotnet test
~~~

Testcontainers starts disposable Keycloak and PostgreSQL instances, imports isolated test realms, and cleans up afterward. The suite tests anonymous access, role checks, invalid/expired tokens, foreign-role isolation, real token exchange, audience rejection, unauthorized requesters and targets, worker permissions, cache concurrency/renewal, safe OAuth errors and cancellation. Browser tests build a fresh complete Compose stack and verify both the Web-to-A-to-B call and the worker's authenticated job. API exchange tests enable a password grant only in their disposable test realm; the checked-in demo realm and browser tests use code + PKCE. It uses the real JWT bearer handler.

See the [test guide](tests/Keycloak.Starter.IntegrationTests/README.md) for the full matrix and fast-test command. The expiration test uses zero clock skew to avoid waiting for the application's normal grace period.

## Configuration

| Setting | Meaning |
| --- | --- |
| `Keycloak__Authority` | Realm issuer URL; required, HTTPS outside Development |
| `Keycloak__Audience` | Expected API audience; required |
| `Keycloak__MetadataAddress` | Optional discovery URL for an internal network |
| `TokenExchange__Enabled` | Enable API A exchange; true in Development/Compose, otherwise false |
| `TokenExchange__ClientId` | Confidential requester, default `starter-api`; must match A audience |
| `TokenExchange__ClientSecret` | API A exchange secret; required when enabled |
| `TokenExchange__Audience` | Fixed supported target `starter-transactions` |
| `TokenExchange__TransactionsUrl` | API B base URL; HTTPS outside Development |
| `Worker__Authority` / `Worker__MetadataAddress` | Canonical issuer / optional internal discovery URL |
| `Worker__ClientId` / `Worker__ClientSecret` | Worker credentials, default client ID `starter-worker` |
| `Worker__ApiUrl` / `Worker__IntervalSeconds` | API A base URL / interval (default 30s) |
| `EXCHANGE_CLIENT_SECRET` / `WORKER_CLIENT_SECRET` | Compose client secrets; must match Keycloak |
| `TRANSACTIONS_PORT` | API B localhost port; default 5160 |
| `KEYCLOAK_PORT` | Compose's localhost Keycloak port; default 8080 |
| `API_PORT` | Compose's localhost API port; default 5150 |

Host development defaults are in `appsettings.Development.json`. Compose sets these values for the API container. PostgreSQL is available only inside the Compose network, and published API/Keycloak ports bind to localhost.

For deployment, provide HTTPS authority and discovery URLs through deployment configuration, rotate credentials, and use a properly configured Keycloak production server. Read [the security and deployment notes](docs/SECURITY.md).

## CI

[GitHub Actions](.github/workflows/ci.yml) restores packages, builds Release with warnings as errors, installs Chromium, runs the Testcontainers and Playwright suites, builds a fresh Docker stack, and runs the development smoke checks. It requires no repository secrets.

## Stop, reset, and troubleshoot

~~~sh
docker compose down
docker compose logs --tail=100 keycloak api transactions worker
~~~

`down` preserves PostgreSQL data. **`docker compose --profile api down --volumes` deletes this project's database, realm changes, and accounts.** Use that only for disposable data. For a named project, include the same `-p` option when stopping or resetting it.

Common issues:

- **401 with a token:** check the issuer, expiry, and `aud`. Use the configured hostname consistently. Never fix this by disabling validation.
- **Token exchange returns 502:** check that the imported `starter-api` client is confidential, exchange-enabled and has B's role scope mapping; `demo-reader` must have B's role. Verify the configured exchange secret. Provider error bodies are intentionally hidden.
- **Worker token acquisition fails:** check its secret in Keycloak and configuration, service-account switch, canonical issuer and internal discovery address. OAuth errors are retried on the next scheduled job, not in a tight loop.
- **Worker/API B gets 401:** verify the audience and canonical issuer. Worker tokens target A; user Web tokens target A; exchanged tokens target B.
- **Missing Web certificate:** run `bash scripts/prepare-web.sh` (or the PowerShell equivalent in the Web guide) before starting the complete stack.
- **403:** check the service account's role assignment, the client's role scope mappings, and `resource_access.starter-api.roles` in a fresh token.
- **Demo token request fails after updating the repository:** an older realm may exist in the volume. Use the separate-project example above or deliberately migrate the realm.
- **Docker test startup fails:** start Docker Desktop, check image registry access, and retry. The tests report container startup logs when initialization fails.
- **Port already in use:** set `API_PORT` and `KEYCLOAK_PORT` before startup.
- **Database password changed in .env:** PostgreSQL initialization scripts run only on an empty volume. Changing environment variables does not update existing database-role passwords.

## Project map

- `src/Keycloak.Starter.Authentication` — shared Keycloak JWT role normalization for both APIs.

- `src/Keycloak.Starter.Transactions` — independent JWT validation and Transactions policy.
- `src/Keycloak.Starter.Worker` — sequential job, cached Client Credentials and bearer handler.
- `src/Keycloak.Starter.Api/Delegation` — focused RFC 8693 service and delegated endpoint.
- `docs/ARCHITECTURE.md` — exact Keycloak configuration and token lifecycles.

- `src/Keycloak.Starter.Api/Program.cs` — composition only; Configuration, Security, Endpoints and OpenApi hold the respective behavior.
- `keycloak/realm-export.json` — reproducible development realm.
- `db/init/01-create-databases.sh` — database owner creation with safe password quoting.
- `tests/Keycloak.Starter.IntegrationTests` — real-token security integration tests.
- `scripts/smoke-test.py` — end-to-end verification of the development stack.
- `docs/SECURITY.md` — trust boundaries and deployment guidance.

## Try the two new scenarios

After the certificate setup, `docker compose up --build -d` starts Web, API A,
API B, worker, Keycloak and PostgreSQL. The original `--profile api` and
`--profile web` invocations still work; services now run by default. To run only
Keycloak/PostgreSQL for host development, name those services explicitly.

1. Open https://localhost:7002 and sign in as `demo-reader` with
   `local-reader-password` (accept/trust the local development certificate).
2. **Profile** and **Reader** call API A with the user's access token.
3. Open **Transactions**. Web calls `GET /accounts/transactions`; A exchanges
   that token and calls B. The page displays HTTP 200 and a response such as:

   ~~~json
   {"sub":"<user-id>","authorizedParty":"starter-api","operation":"read-transactions","transactions":[{"id":"demo-001","amount":42.50,"currency":"MAD"}]}
   ~~~

4. Run `docker compose logs --tail=30 worker`. A successful periodic check logs
   `Synchronization status checked successfully (HTTP 200)` without credentials.
5. Sign in as `demo-unprivileged` / `local-user-password` and open Transactions
   to see 403. Run the integration suite for direct-token rejection and
   unauthorized exchange cases, without manually copying user tokens:

   ~~~sh
   dotnet test --project tests/Keycloak.Starter.IntegrationTests
   ~~~

The three identities are the browser user (`azp=starter-web`), that same user
after exchange (`azp=starter-api`, restricted to B), and the worker service
account (`azp=starter-worker`, `sync` only). Standard Keycloak V2 exchange has
RFC 8693 impersonation semantics, without an `act` actor chain. It provides the
user-on-behalf-of API example here; it is not a consent-based actor delegation
implementation. Client Credentials has no user context and never uses exchange.

To try the worker boundary manually, use the existing curl token example with
`client_id=starter-worker` and `client_secret=local-demo-worker-secret`, then
request `http://localhost:5150/sync/status` (200) and
`http://localhost:5150/accounts/transactions` (403). Do not print or commit the
token. The worker automatically caches and renews its token.

For host execution of the new projects:

~~~sh
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Keycloak.Starter.Transactions
DOTNET_ENVIRONMENT=Development dotnet run --project src/Keycloak.Starter.Worker
~~~

Run API A separately using the existing host instructions. The new projects use
the default host ports (Keycloak 8080, A 5150, B 5160); override the configuration
above when using other ports. See [the architecture document](docs/ARCHITECTURE.md)
for exact realm configuration, token lifecycles, trust boundaries, error mapping,
retry behavior and deployment requirements.

Known limitations: fixed sample transactions (no ownership database),
single-process in-memory worker cache, no immediate JWT revocation chain, no
`act` claims or user consent screen for exchange, and a sequential polling job
rather than durable work processing. Secrets in the realm import are public
local examples; changing environment variables does not rotate realm secrets.
The packaged CLI is a separate future example. Existing Web storage deployment
limits remain documented in [WEB.md](docs/WEB.md).
