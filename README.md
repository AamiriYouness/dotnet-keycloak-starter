# ASP.NET Core + Keycloak Security Starter

[![CI](https://github.com/AamiriYouness/dotnet-keycloak-starter/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/AamiriYouness/dotnet-keycloak-starter/actions/workflows/ci.yml)

[Test coverage and local commands](#tests) · [Security test matrix](tests/Keycloak.Starter.IntegrationTests/README.md)

A .NET 10 reference implementation for browser sign-in, API authorization,
user-on-behalf-of API calls, and background service authentication with Keycloak.
It includes Razor Pages, two minimal APIs, a worker, and tests against real
Keycloak and PostgreSQL containers.

| Scenario | Flow | Demonstrated boundary |
| --- | --- | --- |
| Browser sign-in | Authorization code + PKCE | Secure cookie; tokens stored server-side; API role policies |
| Accounts → Transactions | RFC 8693 standard token exchange | Same user subject; token restricted to the Transactions audience |
| Background synchronization | Client Credentials | Independent service account with only the synchronization permission |

~~~mermaid
flowchart LR
    Browser -->|Secure session cookie| Web[Razor Pages]
    Web -->|User token: Accounts audience| A[Accounts API]
    A -->|Authenticated token exchange| KC[Keycloak]
    KC -->|Transactions token: same user| A
    A -->|Exchanged bearer token| B[Transactions API]
    Worker -->|Client Credentials| KC
    Worker -->|Service token: sync permission| A
    KC --> DB[(PostgreSQL)]
~~~

Start with the local walkthrough below. The [architecture guide](docs/ARCHITECTURE.md)
explains token lifecycles and trust boundaries; the [production reference](docs/PRODUCTION.md)
covers a separate single-server HTTPS deployment with proxies, mounted secrets,
and checked backup/recovery procedures.

## Quick start

Requirements: a .NET 10 SDK and Docker with Compose v2. Windows users should use
Docker Desktop's Linux containers. Python 3 is optional for the smoke script.
Run commands from the repository root, using a fresh project/database to import
the supplied development realm. The default ports must be free; use the
[alternate workstation section](#alternate-workstation-setup-and-existing-realms)
when retaining another local stack.

**macOS / Linux:**

~~~sh
bash scripts/prepare-web.sh
docker compose up --build -d --wait --wait-timeout 180
~~~

**Windows PowerShell:**

~~~powershell
.\scripts\prepare-web.ps1
docker compose up --build -d --wait --wait-timeout 180
~~~

If the browser needs certificate trust, run `dotnet dev-certs https --trust` on
your development machine. Both preparation helpers export the same PFX format.
See [WEB.md](docs/WEB.md) for certificate details and host-run instructions.

| Service | Default address |
| --- | --- |
| Web | https://localhost:7002 |
| Accounts API (API A) | http://localhost:5150 |
| Transactions API (API B) | http://localhost:5160 |
| Keycloak Admin Console | http://localhost:8080/admin/ |
| Accounts Swagger UI | http://localhost:5150/swagger/ |

The local Keycloak administrator is `admin` / `local-admin-password`.
All supplied passwords and client secrets are public development examples.
Copy `.env.example` to `.env` before startup if you need overrides; keep real
credentials out of source control. The default Compose file is for development.

## Browser and worker walkthrough

The development realm includes these browser accounts:

| Username | Password | Reader | Admin | Transactions |
| --- | --- | --- | --- | --- |
| `demo-reader` | `local-reader-password` | 200 | 403 | 200 |
| `demo-admin` | `local-admin-user-password` | 403 | 200 | 403 |
| `demo-unprivileged` | `local-user-password` | 403 | 403 | 403 |

1. Open https://localhost:7002 and sign in as `demo-reader`.
2. Open **Profile** to see the subject returned by Accounts, then **Reader** to
   exercise its role policy.
3. Open **Sample transactions**. Web calls Accounts `/accounts/transactions`;
   Accounts exchanges the user token and calls Transactions. Expect HTTP 200:

   ~~~json
   {"sub":"<user-id>","authorizedParty":"starter-api","operation":"read-transactions","transactions":[{"id":"demo-001","amount":42.50,"currency":"MAD"}]}
   ~~~

4. Sign out, then sign in as `demo-unprivileged` to see protected pages return 403.
5. Observe the independent worker:

   ~~~sh
   docker compose logs --tail=30 worker
   ~~~

   A successful job logs `Synchronization status checked successfully (HTTP 200)`.

Transactions are fixed sample data. This example demonstrates audience and role
authorization, not per-record ownership. The `admin` role does not imply `reader`
or Transactions access. All three browser accounts can access Profile.

The browser user has `azp=starter-web`; the exchanged token retains that user's
`sub` and has `azp=starter-api`. The worker has `azp=starter-worker` and a separate
service-account subject. Standard Keycloak V2 exchange has RFC 8693 impersonation
semantics without an `act` actor chain; it is not consent-based actor delegation.

## Endpoints and permissions

| Endpoint | Required permission | Result |
| --- | --- | --- |
| Accounts `GET /health` | Public | 200 |
| Accounts `GET /me` | Any valid Accounts access token | 200 with `sub` |
| Accounts `GET /reader` | `starter-api` role `reader` | 200, otherwise 403 |
| Accounts `GET /admin` | `starter-api` role `admin` | 200, otherwise 403 |
| Accounts `GET /accounts/transactions` | Approved Web user with `reader`; exchange requires the user's Transactions permission | 200 with the Transactions response |
| Accounts `GET /sync/status` | `starter-worker` client and `sync` role | 200; browser users receive 403 |
| Transactions `GET /transactions` | Transactions audience, `starter-api` authorized party, `read-transactions` role | 200; Accounts-only tokens receive 401 |

Missing or invalid tokens receive **401** with a Bearer challenge. A valid token
without the required permission receives **403**. Accounts exchanges the user
token for a Transactions token; it never forwards the incoming token to that API.

## Keycloak clients and service-account requests

[keycloak/realm-export.json](keycloak/realm-export.json) defines the development realm.

| Client ID | Purpose | Local demo secret |
| --- | --- | --- |
| `starter-web` | Confidential browser client, authorization code + PKCE | `local-demo-web-secret` |
| `starter-api` | Accounts resource and confidential exchange requester | `local-demo-exchange-secret` |
| `starter-transactions` | Transactions resource and read permission | None |
| `starter-worker` | Service account with Accounts `sync` only | `local-demo-worker-secret` |
| `starter-cli` | Curl/smoke service account with Accounts `reader` | `local-demo-reader-secret` |
| `starter-admin` | Curl/smoke service account with Accounts `admin` | `local-demo-admin-secret` |
| `starter-no-role` | Curl/smoke service account without API roles | `local-demo-none-secret` |

`starter-cli` is an OAuth client used by curl and smoke checks; this repository
does not ship a packaged CLI. Password grants are disabled on every imported
client. Only `starter-web` supports interactive login. Scope mappings constrain
each client's API roles; only `starter-api` can request standard token exchange.

Request a reader service token from Bash or zsh (requires curl and Python 3):

~~~sh
TOKEN=$(curl --fail --silent --show-error \
  http://localhost:8080/realms/starter/protocol/openid-connect/token \
  --data-urlencode grant_type=client_credentials \
  --data-urlencode client_id=starter-cli \
  --data-urlencode client_secret=local-demo-reader-secret \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["access_token"])')
curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5150/reader
curl -i -H "Authorization: Bearer $TOKEN" http://localhost:5150/admin
# For Swagger, copy the token before clearing it; see the walkthrough below.
unset TOKEN
~~~

Expect 200 then 403. For the worker boundary, substitute client `starter-worker`
and secret `local-demo-worker-secret`, then call `/sync/status` (200) and
`/accounts/transactions` (403). The running worker caches its token until shortly
before expiry and requests a new Client Credentials token for renewal.

Optional smoke checks exercise anonymous, reader, admin and unprivileged requests
without printing credentials:

~~~sh
python3 scripts/smoke-test.py
~~~

## Swagger and token validation

Open http://localhost:5150/swagger/. Its **Authorize** control accepts a raw access
token and adds the Bearer header.

1. Run the `TOKEN=$(curl ...)` assignment from the service-token example above.
   If you already ran the full example, repeat that assignment because its final
   `unset TOKEN` cleared the shell variable.
2. **Before running `unset TOKEN`**, display the token in your local terminal:

   ~~~sh
   printf '%s\n' "$TOKEN"
   ~~~

3. Copy the raw token, open **Authorize**, paste it without a `Bearer ` prefix,
   and authorize.
4. Run `unset TOKEN` to clear the shell variable, then try `/reader` (200) and
   `/admin` (403) with the reader token.
5. Use **Authorize → Logout** when finished to clear Swagger's copy of the token.

Do not commit tokens or include them in shared screenshots. Swagger and the
OpenAPI document (`/openapi/v1.json`) are available only in Development.

Each API independently validates signature, issuer, audience and lifetime. The
shared `KeycloakJwtBearerEvents` handler maps only the current API's
`resource_access.<audience>.roles` into application-owned role claims. Policies
use `RequireRole`; realm roles, foreign-client roles and incoming application-role
claims cannot bypass that mapping. Malformed role data grants no permissions.

In development Compose, the browser uses `http://localhost:8080` while containers
retrieve metadata through `http://keycloak:8080`. Keycloak retains the canonical
issuer and advertises reachable backchannel endpoints. Issuer validation stays
enabled. The production reference uses canonical HTTPS URLs throughout.

## Run applications on the host

The same default addresses apply to Compose and host development: Keycloak 8080,
Accounts 5150, Transactions 5160, Web HTTPS 7002. Follow the complete
[host-run walkthrough](docs/WEB.md#run-web-on-the-host), including the User Secrets
command and explicit host-reachable authority, metadata and API settings.

To run Accounts or Transactions on the host, start only their dependencies and
use separate terminals:

~~~sh
docker compose up -d --wait --wait-timeout 180 postgres keycloak
# Terminal 1
Keycloak__Authority=http://localhost:8080/realms/starter \
TokenExchange__TransactionsUrl=http://localhost:5160 \
dotnet run --project src/Keycloak.Starter.Api --launch-profile http
# Terminal 2
Keycloak__Authority=http://localhost:8080/realms/starter \
dotnet run --project src/Keycloak.Starter.Transactions --launch-profile http
# Terminal 3 (optional worker)
DOTNET_ENVIRONMENT=Development \
Worker__Authority=http://localhost:8080/realms/starter \
Worker__ApiUrl=http://localhost:5150 \
dotnet run --project src/Keycloak.Starter.Worker
~~~

Do not start a container and host process on the same published application port.

## Tests

Requirements: .NET 10, Docker, and Playwright Chromium for browser tests. Follow
[the browser installation steps](docs/WEB.md#browser-tests) on first use.

~~~sh
dotnet test
~~~

Testcontainers imports isolated realms and starts disposable Keycloak/PostgreSQL
instances. The suite covers token validation, scoped role mapping, exchange
permissions, worker boundaries, token renewal/concurrency and cancellation.
Browser tests run development and Production-mode Compose stacks and verify
login, PKCE, refresh, logout, secure cookies, container recreation, delegated calls,
proxy trust, and checked backup/restore operations. API tests enable a password
grant only in their disposable realm; the demo realm and browser tests use PKCE.

For API tests alone:

~~~sh
dotnet test --project tests/Keycloak.Starter.IntegrationTests
~~~

[GitHub Actions](.github/workflows/ci.yml) builds Release with warnings as errors,
runs the shell checks, installs Chromium, executes the tests and runs the smoke
checks. See [the integration test guide](tests/Keycloak.Starter.IntegrationTests/README.md).

## Configuration

| Setting | Meaning |
| --- | --- |
| `Keycloak__Authority` | Canonical realm issuer; HTTPS outside Development |
| `Keycloak__Audience` | Expected Accounts API audience |
| `Keycloak__MetadataAddress` | Optional discovery URL; host runs need a host-reachable address |
| `TokenExchange__Enabled` | Enabled in Development/Compose; otherwise opt in |
| `TokenExchange__ClientId` / `TokenExchange__ClientSecret` | Accounts exchange credentials; client ID must match its audience |
| `TokenExchange__Audience` / `TokenExchange__TransactionsUrl` | Transactions audience and base URL |
| `Worker__Authority` / `Worker__MetadataAddress` | Worker issuer and optional discovery URL |
| `Worker__ClientId` / `Worker__ClientSecret` | Worker client credentials |
| `Worker__ApiUrl` / `Worker__IntervalSeconds` | Accounts base URL / polling interval (default 30s) |
| `KEYCLOAK_PORT`, `API_PORT`, `TRANSACTIONS_PORT`, `WEB_PORT` | Compose host ports: 8080, 5150, 5160, 7002 |
| `WEB_CLIENT_SECRET`, `EXCHANGE_CLIENT_SECRET`, `WORKER_CLIENT_SECRET` | Compose secrets; values must match Keycloak |

The checked-in `appsettings.Development.json` files use the default host addresses.
Production uses separate configuration, HTTPS and deployment credentials; follow
[PRODUCTION.md](docs/PRODUCTION.md) and [SECURITY.md](docs/SECURITY.md).

## Alternate workstation setup and existing realms

Use this section only when the default ports are occupied or a realm already
exists. Startup import skips an existing realm; editing the JSON file does not
update its clients or users. Preserve its data and use reviewed configuration
changes, or create a separate disposable project for a clean demonstration.

For example, run the backend services on alternate ports:

~~~sh
KEYCLOAK_PORT=8082 API_PORT=5152 TRANSACTIONS_PORT=5162 \
docker compose -p starter-alternate up -d --build --wait --wait-timeout 180 \
  postgres keycloak api transactions worker
KEYCLOAK_URL=http://localhost:8082 API_URL=http://localhost:5152 \
python3 scripts/smoke-test.py
~~~

The full Web host overrides for these ports are in
[WEB.md](docs/WEB.md#alternate-workstation-ports-and-existing-realms).
Docker Desktop users with multiple engines can explicitly select Desktop:

~~~sh
export DOCKER_CONTEXT=desktop-linux
export DOCKER_HOST=unix://$HOME/.docker/run/docker.sock
~~~

These macOS paths are an optional workstation example, not a requirement for other
platforms. `DOCKER_CONTEXT` selects the CLI/Compose engine; `DOCKER_HOST` supplies
the Desktop socket to Testcontainers. Use your platform's normal Docker settings
when only one engine is configured.

## Troubleshooting and limits

~~~sh
docker compose down
docker compose logs --tail=100 keycloak api transactions worker
~~~

`down` preserves data. `docker compose down --volumes` deletes the selected
project's database and sessions; use it only for disposable data. Include the same
`-p` option when operating a named project.

- **401:** check issuer, audience and expiry; keep validation enabled.
- **403:** check the required client role and scope mapping. Admin and reader are independent.
- **Exchange returns 502:** check Accounts' confidential-client secret, exchange switch, scope mappings and the user's Transactions role.
- **Worker acquisition fails:** check its secret, service-account switch and discovery address.
- **Browser login fails on the host:** verify User Secrets and the host-run authority/metadata/API overrides; `.env` values are not automatically loaded by `dotnet run`.
- **Missing Web certificate:** run the preparation script before Compose. Retain the certificate with the authentication volume.
- **Changed realm or database credentials:** startup import and PostgreSQL initialization do not reapply configuration to existing volumes.

The Transactions response is fixed sample data. Resource ownership authorization
and an enforced MFA enrollment/login demonstration are separate follow-up examples;
they are not implemented here. Other limits include a single Web instance and
refresh lock, an in-memory worker token cache, no immediate JWT revocation chain,
and no durable job scheduler. See the architecture and production guides for the
precise deployment scope.

## Project map

| Location | Responsibility |
| --- | --- |
| `src/Keycloak.Starter.Web` | Razor Pages, OIDC login, persistent server-side tickets |
| `src/Keycloak.Starter.Api` | Accounts endpoints, role policies, token exchange |
| `src/Keycloak.Starter.Transactions` | Independent Transactions audience and role validation |
| `src/Keycloak.Starter.Worker` | Scheduled synchronization checks and cached service tokens |
| `src/Keycloak.Starter.Authentication` | Shared JWT role mapping and trusted-proxy configuration |
| `keycloak/realm-export.json` | Development clients, roles, users and audience mappings |
| `deploy/production` | HTTPS deployment, secret preparation and backup scripts |
| `tests` | Real-server OAuth, browser and operational checks |

## License

The starter is available under the [MIT License](LICENSE), including commercial
use, modification and redistribution subject to its notice requirements. Bundled
frontend libraries retain their own copyright and license notices under
`src/Keycloak.Starter.Web/wwwroot/lib`. The starter is provided without warranty.
