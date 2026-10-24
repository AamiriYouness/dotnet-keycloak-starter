# Integration tests

Run from the repository root with .NET 10 and Docker Desktop (or another compatible Docker engine) running:

```sh
dotnet test Keycloak.Starter.sln
```

Testcontainers starts Keycloak 26.8.1 and PostgreSQL 17.11 on a private Docker network, assigns random host ports, imports the test realms, and removes the containers and network afterward. The first run downloads the images. Docker Compose and the manually configured development realm are not required.

The realm JSON under `TestData` contains disposable test clients and credentials. It is not a development or production realm export. Tokens are requested through the client credentials flow; these tests represent service accounts rather than interactive user login.

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
dotnet test Keycloak.Starter.sln --filter-not-trait Category=Containers
```
