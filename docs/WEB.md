# Razor Pages and browser authentication

The Web application signs users in with authorization code + PKCE (S256), keeps
tokens in encrypted server-side tickets, and uses a Secure/HttpOnly session
cookie. Accounts and Transactions independently enforce API permissions.

## Run the browser demo

Requirements: .NET 10 and Docker with Compose v2. Run from the repository root.
The default ports are the same for Compose and host development:

| Service | Default address |
| --- | --- |
| Web | https://localhost:7002 |
| Keycloak | http://localhost:8080 |
| Accounts | http://localhost:5150 |
| Transactions | http://localhost:5160 |

**macOS / Linux:**

~~~sh
bash scripts/prepare-web.sh
docker compose up -d --build --wait --wait-timeout 180
~~~

**Windows PowerShell, using Docker Desktop's Linux containers:**

~~~powershell
.\scripts\prepare-web.ps1
docker compose up -d --build --wait --wait-timeout 180
~~~

Open https://localhost:7002. If needed, trust your development certificate with
`dotnet dev-certs https --trust`. The development realm includes:

| Username | Password | Reader | Admin | Transactions |
| --- | --- | --- | --- | --- |
| `demo-reader` | `local-reader-password` | 200 | 403 | 200 |
| `demo-admin` | `local-admin-user-password` | 403 | 200 | 403 |
| `demo-unprivileged` | `local-user-password` | 403 | 403 | 403 |

Sign in as `demo-reader`, open Profile and Reader, then Transactions. Profile
shows the authenticated subject; Transactions calls Accounts, which exchanges
the user's token before calling the Transactions API. The transaction response
is fixed sample data, not a demonstration of record ownership.

Sign out and use the other accounts to explore 403 responses. The links remain
visible so users can inspect authorization behavior; hiding a link would not
enforce permission. All three accounts can access Profile. The public
**Privacy and demo data** page explains session and token handling before login.

## Run Web on the host

This path runs the backend services in Docker and Web through `dotnet run`.
Do not also run the Web container on port 7002. If the default Web container is
already running, stop it first:

~~~sh
docker compose stop web
~~~

For a **fresh development realm**, prepare the certificate, start the backend,
and store the checked-in Web demo secret outside the project tree:

~~~sh
bash scripts/prepare-web.sh
docker compose up -d --build --wait --wait-timeout 180 \
  postgres keycloak api transactions worker
dotnet user-secrets set "Keycloak:ClientSecret" "local-demo-web-secret" \
  --project src/Keycloak.Starter.Web
~~~

The project already defines `UserSecretsId`; no `user-secrets init` is necessary.
This value must match the `starter-web` client in the selected realm. Do not
replace an existing realm's custom secret with the public demo secret.
User Secrets is development storage, not an encrypted production secret store.
See [Microsoft's Secret Manager documentation](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

Start Web with explicit host-reachable settings:

~~~sh
Keycloak__Authority=http://localhost:8080/realms/starter \
Keycloak__MetadataAddress=http://localhost:8080/realms/starter/.well-known/openid-configuration \
Api__BaseUrl=http://localhost:5150 \
DataProtection__CertificatePath="$PWD/.local/certs/web.pfx" \
DataProtection__CertificatePassword=local-demo-certificate-password \
dotnet run --project src/Keycloak.Starter.Web --launch-profile https
~~~

These commands use the default certificate password. If you customized it during
preparation, supply that same password. The launch profile selects Development
and HTTPS port 7002. Kestrel uses the installed ASP.NET Core development
certificate; the supplied PFX protects the persisted ticket key ring.

**PowerShell equivalent:**

~~~powershell
.\scripts\prepare-web.ps1
docker compose up -d --build --wait --wait-timeout 180 postgres keycloak api transactions worker
dotnet user-secrets set "Keycloak:ClientSecret" "local-demo-web-secret" --project src/Keycloak.Starter.Web
$env:Keycloak__Authority = "http://localhost:8080/realms/starter"
$env:Keycloak__MetadataAddress = "http://localhost:8080/realms/starter/.well-known/openid-configuration"
$env:Api__BaseUrl = "http://localhost:5150"
$env:DataProtection__CertificatePath = Join-Path (Get-Location) ".local/certs/web.pfx"
$env:DataProtection__CertificatePassword = "local-demo-certificate-password"
dotnet run --project src/Keycloak.Starter.Web --launch-profile https
~~~

Compose `.env` values are not automatically loaded by `dotnet run`. A host Web
process must not use `http://api:8080` or `http://keycloak:8080`; those names are
for containers. The callbacks registered in Keycloak must exactly match
`https://localhost:7002/signin-oidc` and
`https://localhost:7002/signout-callback-oidc`. Changing Web's port also requires
updating both registrations.

For running the APIs and worker on the host too, use the
[README host commands](../README.md#run-applications-on-the-host). Keep only one
process bound to each application port.

## Alternate workstation ports and existing realms

This is optional; it does not change the default path above. A workstation with
an existing named `starter-demo` stack on Keycloak 8082 / Accounts 5152 can retain
those ports and its data. To run Web on the host, stop only that named stack's
Web container first (its backend and volumes remain available):

~~~sh
docker compose -p starter-demo stop web
~~~

Point host Web at that realm and API explicitly:

~~~sh
Keycloak__Authority=http://localhost:8082/realms/starter \
Keycloak__MetadataAddress=http://localhost:8082/realms/starter/.well-known/openid-configuration \
Api__BaseUrl=http://localhost:5152 \
DataProtection__CertificatePath="$PWD/.local/certs/web.pfx" \
DataProtection__CertificatePassword=local-demo-certificate-password \
dotnet run --project src/Keycloak.Starter.Web --launch-profile https
~~~

Use the existing realm's Web secret in User Secrets. The local development helper
`python3 scripts/configure-web-demo.py --project starter-demo` can preserve that
secret and write it to User Secrets and the ignored local `.env` file. It also
configures Web client mappings and demo users; it is a development-only setup
helper, not a general realm migration tool. It does not configure the exchange
requester, Transactions client or worker.

Startup import skips existing realms. To exercise all flows, the selected realm
must contain the clients, scopes and role assignments described in
[ARCHITECTURE.md](ARCHITECTURE.md). Preserve existing data and apply reviewed
configuration changes, or use a fresh isolated project from the
[alternate setup example](../README.md#alternate-workstation-setup-and-existing-realms).
The Web process can retain port 7002 only if no other Web instance occupies it.

## Certificate preparation

PFX (PKCS#12) contains a certificate and private key and works across Windows,
macOS and Linux. PowerShell 7 on macOS/Linux can run `pwsh scripts/prepare-web.ps1`.
Both helpers preserve an existing PFX. See [Microsoft's Docker HTTPS examples](https://learn.microsoft.com/en-us/aspnet/core/security/docker-compose-https).

The setup has four steps:

1. **Host preparation:** the helper exports your ASP.NET Core development
   certificate to `.local/certs/web.pfx`. It requires the host SDK and runs before
   Compose, not inside the Dockerfile.
2. **Image build:** the Dockerfile publishes Web and prepares its authentication
   directory. Certificates/private keys are excluded from the image build context.
3. **Compose initialization:** `web-cert-init` copies the host PFX into the
   `web-certs` volume with ownership and permissions for the non-root Web user.
4. **Application startup:** Kestrel reads that volume at `/https`; Data Protection
   uses the same certificate for this local Compose demonstration.

Export and trust are separate: the PFX supplies a private key to the container;
local trust tells the browser to accept the certificate. Containers do not change
browser trust. The helpers read `HTTPS_CERT_PASSWORD` from their process
environment, falling back to the public demo password. Compose reads its value
from its environment or `.env`; keep those values consistent. Changing the
password variable does not change an existing PFX's password.

Development certificates/passwords are not deployment credentials. The
[production reference](PRODUCTION.md) uses HTTPS proxies and a separate persistent
Data Protection certificate.

## Configuration and sessions

`Program.cs` composes services and middleware. The implementation uses:

- `Configuration/WebSettings.cs`: typed Keycloak, API, storage and certificate settings.
- `Configuration/WebOptionsExtensions.cs`: binding, environment rules and startup validation.
- `Security/KeycloakAuthenticationExtensions.cs`: cookies, OIDC and outbound API clients.
- `Security/AuthenticationStorage.cs` and `AuthenticationStorageExtensions.cs`: directory permissions, certificate loading, Data Protection and the ticket store.

Invalid settings fail startup with setting names, without printing the secret.
HTTP authority/API URLs are accepted only in Development. Production requires an
explicit persistent storage path and a readable private key in the protecting
PFX. Development storage defaults to `.local/authentication` under Web's content
root. The refresh threshold defaults to 30 seconds (allowed range 1–300).

The browser receives an encrypted session-reference cookie; access, refresh and
ID tokens stay in encrypted server-side tickets. The authentication volume holds
both tickets and the key ring. Preserve it **and the original protecting PFX and
password** across recreation. Losing either invalidates readable sessions.

Sessions have a fixed maximum lifetime of eight hours. Web refreshes access tokens
before expiry; rejected refresh grants require login again. Sign-out uses an
antiforgery-protected POST, removes the local ticket and signs out of Keycloak.
A refresh cannot recreate a ticket removed by sign-out.

The file store and refresh lock support one Web instance. Multiple replicas need
shared transactional storage and distributed refresh coordination. Certificate
rotation must retain access to old decryption certificates.

## Browser tests

The Playwright project creates isolated development and Production-mode Compose
stacks with unused host ports, temporary certificates/secrets and disposable
realms. It does not use the workstation's configured realm or User Secrets.

On first use:

~~~sh
dotnet build Keycloak.Starter.sln -c Release
PLAYWRIGHT_INSTALL_BROWSERS=1 dotnet test --project tests/Keycloak.Starter.EndToEndTests -c Release --no-build
~~~

PowerShell can set `$env:PLAYWRIGHT_INSTALL_BROWSERS = "1"` before the same test
command. On Linux, install browser dependencies using:

~~~sh
pwsh tests/Keycloak.Starter.EndToEndTests/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
~~~

Then run the suite normally:

~~~sh
dotnet test Keycloak.Starter.sln -c Release
~~~

The browser journeys verify login/PKCE, role checks, Transactions exchange,
refresh, logout, CSRF, local return URLs, secure cookies, session survival after
container recreation, and public access to the demo data-handling page.
The production fixture also verifies proxy isolation and operational scripts.

Only the disposable browser contexts ignore test-certificate trust errors;
application outbound HTTPS retains certificate validation. Fixture cleanup removes
its containers, networks, volumes and certificates. If interrupted, clean up only
the reported disposable project with its matching Compose configuration.
