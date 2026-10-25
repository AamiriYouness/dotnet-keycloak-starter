# Integration tests

Run from the repository root with .NET 10 and Docker Desktop (or another compatible Docker engine) running:

```sh
dotnet test --project tests/Keycloak.Starter.IntegrationTests
```

Testcontainers starts Keycloak 26.8.1 and PostgreSQL 17.11 on a private Docker network, assigns random host ports, imports the test realms, and removes the containers and network afterward. The first run downloads the images. Docker Compose and the manually configured development realm are not required.

The realm JSON under `TestData` contains disposable test clients and credentials. It is not a development or production realm export. The original fixture requests service-account tokens through Client Credentials. The separate DelegationFixture derives its realm from the checked-in demo import, enabling a test-only password grant on starter-web to create real user sessions without browser automation. Production password grants remain disabled. The browser suite independently exercises authorization code + PKCE through the full Compose stack.

| Case | Expected result |
| --- | --- |
| Public health endpoint | 200 |
| Protected endpoint without a token | 401 with a Bearer challenge |
| Valid token on `/me` | 200 with the token's subject |
| Reader on `/reader`; admin on `/admin` | 200 |
| Reader on `/admin`; admin on `/reader` | 403 |
| No API roles, or roles belonging to another client or the realm | 403 |
| Malformed `resource_access` | 403 |
| Wrong audience, untrusted realm, expired token, modified payload, or malformed JWT | 401 |
| Production startup with HTTP authority/discovery or invalid settings | Startup rejected |
| OpenAPI document and Swagger UI | 200 in Development; 404 in Production |
| OpenAPI authentication | HTTP Bearer JWT scheme on protected operations; health remains public |

The API's real JWT bearer handler validates signatures, issuer, audience, and lifetime. Authentication is not replaced with a fake test handler. The fixture sets clock skew to zero so the short-lived-token test can exercise expiration promptly; the application retains its normal clock skew.

The tests intentionally keep `reader` and `admin` independent: an admin does not inherit reader access unless both roles are assigned.

Run only the fast tests that do not require Docker:

```sh
dotnet test --project tests/Keycloak.Starter.IntegrationTests/Keycloak.Starter.IntegrationTests.csproj --filter-not-trait Category=Containers
```


The delegation/worker security matrix also covers:

| Case | Expected result |
| --- | --- |
| Web user token on A `/me` | 200 |
| Original A-only user token on B | 401 |
| A exchanges user token and calls B | 200; subject preserved; B-only audience |
| Exchange by Web/worker, wrong secret, or unauthorized target | OAuth error |
| Unprivileged user on delegation endpoint | 403 |
| Worker Client Credentials on sync | 200 |
| Worker on reader/admin/user-delegated endpoint | 403 |
| User on worker endpoint | 403 |
| 20 concurrent service token requests | One token POST |
| Token renewal threshold | Reuses token before threshold; renews afterward |
| Invalid/malformed OAuth responses | Safe exception without provider content |
| Cancellation during discovery, token POST, gate wait or API call | Operation canceled |

The new fixture uses disposable Keycloak development storage; the original fixture
also validates the PostgreSQL-backed setup. No manually configured server is used.
On this workstation select Docker Desktop for both clients:

```sh
export DOCKER_CONTEXT=desktop-linux
export DOCKER_HOST=unix://$HOME/.docker/run/docker.sock
dotnet test
```

The complete solution additionally requires Playwright Chromium, as documented
in [the Web guide](../../docs/WEB.md).


`KeycloakRoleMappingTests` covers the shared post-validation handler: each API
receives only its resource roles; malformed/missing roles grant nothing; realm,
foreign and incoming application-role claims cannot bypass mapping; repeated
mapping does not duplicate roles. The container tests exercise this handler
through real JWT validation and the APIs' `RequireRole` policies.
