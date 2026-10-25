# Razor Pages and browser authentication

The web application uses authorization code with PKCE (S256), a confidential Keycloak client, and a Secure/HttpOnly authentication cookie. It calls the API with the signed-in user's access token. The API remains the authority for reader/admin permissions.

## Configuration and startup

`Program.cs` contains service registration and the HTTP pipeline. The implementation is organized as follows:

- `Configuration/WebSettings.cs` defines typed settings for Keycloak, the API, authentication storage, and certificate protection.
- `Configuration/WebOptionsExtensions.cs` binds those sections and applies data annotations, environment-dependent rules, and `ValidateOnStart`.
- `Security/KeycloakAuthenticationExtensions.cs` configures cookies, OpenID Connect, and API clients using the validated settings.
- `Security/AuthenticationStorage.cs` owns directory preparation, permissions, and the loaded certificate. `AuthenticationStorageExtensions.cs` registers the framework Data Protection provider and persistent ticket store.

Invalid settings stop startup with structured options-validation failures. Messages name the invalid settings without printing the client secret. HTTP authority/API URLs are accepted only in Development. Production requires an existing certificate file. Storage paths must be absolute; the default remains `.local/authentication` under the web application's content root. The refresh threshold defaults to 30 seconds and accepts values from 1 through 300.

## Run the web demo

With .NET 10 and Docker running, prepare a development certificate on your host, then start the web profile. Run these commands from the repository root.

**macOS / Linux (Bash):**

~~~sh
bash scripts/prepare-web.sh
docker compose --profile web up -d --build --wait --wait-timeout 180
~~~

**Windows (PowerShell, using Docker Desktop's Linux containers):**

~~~powershell
.\scripts\prepare-web.ps1
docker compose --profile web up -d --build --wait --wait-timeout 180
~~~

PowerShell 7 users on macOS/Linux can also run `pwsh scripts/prepare-web.ps1`. Both helpers export the same PFX format and preserve an existing certificate.

Open https://localhost:7002. Trust your .NET development certificate locally if your browser requires it (`dotnet dev-certs https --trust`). The fresh stack uses Keycloak at http://localhost:8080 and the API at http://localhost:5150.

### What the PFX does, and where the scripts run

PFX (PKCS#12) is a cross-platform bundle containing a certificate and its private key. It is supported by .NET on Windows, macOS, and Linux; the web image in this project is a Linux image. See [Microsoft's Docker HTTPS examples for each operating system](https://learn.microsoft.com/en-us/aspnet/core/security/docker-compose-https).

The setup has four distinct steps:

1. **Host preparation:** `prepare-web.sh` or `prepare-web.ps1` exports your local ASP.NET Core development certificate to `.local/certs/web.pfx`. This requires the .NET SDK on the host and runs before Compose. It is not executed by the Dockerfile.
2. **Image build:** the Dockerfile publishes the application and creates its authentication directory. It does not generate or copy a certificate/private key into an image. The `.dockerignore` excludes `.local`.
3. **Compose initialization:** the one-off `web-cert-init` Linux container copies the host PFX into the `web-certs` volume and sets ownership and file permissions for the non-root web application user.
4. **Application startup:** the web container mounts that volume read-only at `/https`. Kestrel loads the PFX for HTTPS. Data Protection uses the same certificate to encrypt its persisted key ring for this local demonstration.

Trust and export are separate: exporting a PFX gives the container its certificate/private key; trusting the development certificate tells your host/browser to accept its issuer. Containers do not configure browser trust.

The helpers read `HTTPS_CERT_PASSWORD` from their process environment, falling back to the public local-demo password. Docker Compose reads its value from the environment or `.env`. If you customize the password, supply the same value to both. Neither helper replaces an existing PFX automatically; changing the password variable does not change that file's password.

These development certificates and sample passwords are not deployment credentials. In production, supply a deployment certificate through your secret-management process (or terminate TLS at a trusted reverse proxy), and use a separate managed certificate/key for Data Protection. Preserve access to old decryption certificates when rotating keys.

For this repository's existing named demo, retain its ports and data:

~~~sh
python3 scripts/configure-web-demo.py --project starter-demo
KEYCLOAK_PORT=8082 API_PORT=5152 docker compose -p starter-demo --profile web up -d --build --wait --wait-timeout 180
~~~

The configuration script adds missing demo users and web client scope mappings. It preserves existing users and the web client's secret, saving that secret in User Secrets and ignored local Compose configuration. It does not export your actual secret into the checked-in realm.

| User | Local demonstration password | API role |
| --- | --- | --- |
| demo-reader | local-reader-password | reader |
| demo-admin | local-admin-user-password | admin |
| demo-unprivileged | local-user-password | None |

These public demo credentials are for disposable local environments.

The page links are visible to signed-in users so they can explore permissions. The server forwards requests to the API; an API 403 produces an access-denied page with status 403. Hiding a link is not an authorization control.

## Run on the host

The development settings target the existing demo's Keycloak port 8082 and API port 5152. Store the client secret using User Secrets, then:

~~~sh
dotnet run --project src/Keycloak.Starter.Web --launch-profile https
~~~

For other ports, override `Keycloak__Authority` and `Api__BaseUrl`. Keep the exact login and logout callback addresses registered in Keycloak. The default callbacks are https://localhost:7002/signin-oidc and https://localhost:7002/signout-callback-oidc.

## Sessions and container recreation

The browser receives an encrypted reference cookie. Access, refresh, and ID tokens remain in server-side authentication tickets. Tickets are encrypted with ASP.NET Core Data Protection and written atomically to the persistent authentication volume.

The same volume holds the key ring. The application name is fixed at `Keycloak.Starter.Web`. Compose protects the key ring at rest using the mounted certificate and restricts the authentication directory to the non-root application user.

Keep **both the authentication volume and its protecting certificate** across recreation. Losing either invalidates existing sessions. Deleting Compose volumes deliberately deletes sessions as well as the database. Certificate rotation requires retaining old decryption certificates; plan this separately for a deployment.

Sessions have a fixed maximum lifetime of eight hours. Access tokens are refreshed server-side before expiry. Rejected refresh grants remove the local session and require a new login. Logout uses an antiforgery-protected POST, removes the server ticket, and signs out of Keycloak. A completed refresh cannot recreate a session already removed by logout.

This file ticket store and its refresh lock target a **single web instance**. A horizontally scaled deployment needs a shared transactional ticket store and distributed refresh coordination. Use managed secret storage and a dedicated protected Data Protection certificate for a deployed environment.

## Browser tests

The .NET Playwright project starts a fresh, independently named Compose stack using unused localhost ports, a generated test certificate, and the checked-in demo realm. It does not use your local client secret or accounts.

Build and install Chromium on first use:

~~~sh
dotnet build Keycloak.Starter.sln -c Release
PLAYWRIGHT_INSTALL_BROWSERS=1 dotnet test --project tests/Keycloak.Starter.EndToEndTests/Keycloak.Starter.EndToEndTests.csproj -c Release --no-build
~~~

On Linux, install browser system dependencies using the generated Playwright script:

~~~sh
pwsh tests/Keycloak.Starter.EndToEndTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
~~~

After installation, run the whole suite normally:

~~~sh
dotnet test Keycloak.Starter.sln -c Release
~~~

The tests exercise real Keycloak login, PKCE, user roles, logout/SSO, CSRF protection, local return URLs, access-token refresh, secure cookie flags, and session survival after **container recreation**. They also check ticket persistence, expiry, and durable logout.

Browser contexts ignore certificate trust errors only for the disposable localhost test certificate. The application retains normal TLS validation for its outbound requests. Browser authentication state is not saved to source files. Test containers, volumes, networks, and generated certificates are removed after the fixture.
If a run is forcibly terminated before fixture cleanup, use the reported project name with `docker compose -p starter-e2e-... --profile web down --volumes --remove-orphans` to remove that disposable stack.

For the fast API tests without browser/container fixtures, use:

~~~sh
dotnet test --project tests/Keycloak.Starter.IntegrationTests/Keycloak.Starter.IntegrationTests.csproj --filter-not-trait Category=Containers
~~~

## Delegated Transactions page

The authenticated `/Transactions` page calls API A `/accounts/transactions`.
Sign in as `demo-reader` to exercise standard V2 exchange and the separate
Transactions API. The default Compose stack now starts both APIs and the worker
with Web; run the same certificate preparation first. Existing database volumes
need an explicit realm migration or a fresh isolated project for the new clients
and mappings. `configure-web-demo.py` updates only the original Web example; it
does not migrate the new exchange/worker configuration. See [ARCHITECTURE.md](ARCHITECTURE.md).


For deployment behind an HTTPS proxy, use [PRODUCTION.md](PRODUCTION.md).
Production now requires an explicit `Authentication:StoragePath` and validates
that the key-protection PFX has a readable private key. Its certificate is separate
from the public TLS certificate and must survive Web recreation with the key ring.
