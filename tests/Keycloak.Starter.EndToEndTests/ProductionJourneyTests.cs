using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Keycloak.Starter.EndToEndTests;

[Trait("Category", "Browser")]
public sealed class ProductionJourneyTests(ProductionWebFixture fixture)
    : BrowserJourneys(fixture), IClassFixture<ProductionWebFixture>
{
    [Fact]
    public async Task Public_proxy_redirects_to_https_and_blocks_admin_and_master_realm()
    {
        using var client = fixture.CreateHttpClient();
        using var redirect = await client.GetAsync($"http://app.localhost:{fixture.HttpPort}/Profile", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PermanentRedirect, redirect.StatusCode);
        Assert.Equal(fixture.WebUrl + "/Profile", redirect.Headers.Location?.ToString());
        foreach (var path in new[] { "/admin/", "/realms/master/.well-known/openid-configuration", "/health/ready", "/metrics" })
        {
            using var denied = await client.GetAsync(fixture.KeycloakUrl + path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }
    }

    [Fact]
    public async Task Proxy_overwrites_spoofed_forwarded_headers_and_keeps_https_login_callback()
    {
        using var client = fixture.CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, fixture.WebUrl + "/Profile");
        request.Headers.Add("X-Forwarded-Proto", "http");
        request.Headers.Add("X-Forwarded-Host", "attacker.invalid");
        request.Headers.Add("X-Forwarded-For", "203.0.113.99");
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(fixture.KeycloakUrl + "/realms/starter/", response.Headers.Location?.ToString());
        Assert.DoesNotContain("attacker.invalid", response.Headers.Location!.ToString());
        Assert.All(response.Headers.GetValues("Set-Cookie"), cookie => Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Worker_permissions_and_production_api_surface_are_enforced_through_tls_proxy()
    {
        using var publicClient = fixture.CreateHttpClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = "starter-worker", ["client_secret"] = fixture.WorkerSecret
        });
        using var grant = await publicClient.PostAsync(fixture.KeycloakUrl + "/realms/starter/protocol/openid-connect/token", form, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        using var token = JsonDocument.Parse(await grant.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        using var api = fixture.CreateHttpClient(internalProxy: true);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
        using var allowed = await api.GetAsync("https://accounts.localhost/sync/status", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using var forbidden = await api.GetAsync("https://accounts.localhost/accounts/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var wrongAudience = await api.GetAsync("https://transactions.localhost/transactions", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);
        foreach (var path in new[] { "/swagger/index.html", "/openapi/v1.json" })
        {
            using var missing = await api.GetAsync("https://accounts.localhost" + path, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        Assert.DoesNotContain(fixture.WorkerSecret, await fixture.WorkerLogsAsync());
    }

    [Fact]
    public async Task Every_service_secret_mount_is_read_only()
    {
        foreach (var service in new[] { "gateway", "internal-proxy", "web", "api", "transactions", "worker", "keycloak", "postgres" })
        {
            var mounts = await fixture.ComposeAsync("exec", "-T", service, "cat", "/proc/mounts");
            var secrets = mounts.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(fields => fields.Length > 3 && fields[1].StartsWith("/run/secrets/", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(secrets);
            Assert.All(secrets, fields => Assert.Contains("ro", fields[3].Split(',')));
        }
    }

    [Fact]
    public async Task Administrative_provisioning_works_inside_keycloak_without_public_admin_routes()
    {
        var clients = await fixture.ComposeAsync("exec", "-T", "keycloak", "bash", "-ec",
            "KC_CLI_PASSWORD=$(cat /run/secrets/keycloak-admin-password) /opt/keycloak/bin/kcadm.sh config credentials " +
            "--server http://localhost:8080 --realm master --user \"$KC_BOOTSTRAP_ADMIN_USERNAME\" --config /tmp/provisioning-check.config >/dev/null && " +
            "/opt/keycloak/bin/kcadm.sh get clients -r starter -q clientId=starter-web --fields clientId --config /tmp/provisioning-check.config");
        Assert.Contains("starter-web", clients);
    }

    [Fact]
    public async Task Web_key_ring_can_be_archived_and_extracted_for_recovery()
    {
        using var client = fixture.CreateHttpClient();
        using var challenge = await client.GetAsync(fixture.WebUrl + "/Profile", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        await fixture.ComposeAsync("exec", "-T", "web", "bash", "-ec",
            "tar -C /app/authentication -czf /tmp/web-auth-drill.tar.gz . && mkdir /tmp/web-auth-drill && " +
            "tar -C /tmp/web-auth-drill -xzf /tmp/web-auth-drill.tar.gz && " +
            "test -n \"$(find /tmp/web-auth-drill/keys -name '*.xml' -print -quit)\"");
    }

    [Fact]
    public async Task Checked_backup_script_produces_restorable_database_and_key_ring()
    {
        using var client = fixture.CreateHttpClient();
        using var challenge = await client.GetAsync(fixture.WebUrl + "/Profile", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var backup = await fixture.CreateCheckedBackupAsync();
        Assert.True(File.Exists(Path.Combine(backup, "COMPLETE")));
        Assert.False(File.Exists(Path.Combine(backup, "INCOMPLETE")));
        foreach (var file in new[] { "keycloak.dump", "web-auth.tar.gz", "secrets.tar.gz", "deployment-model.json", "images.json" })
            Assert.True(new FileInfo(Path.Combine(backup, file)).Length > 0);
        await fixture.ComposeAsync("cp", Path.Combine(backup, "keycloak.dump"), "postgres:/tmp/checked-backup.dump");
        var clients = await fixture.ComposeAsync("exec", "-T", "postgres", "sh", "-ec",
            "createdb -U postgres -O keycloak checked_backup_restore && " +
            "pg_restore -U postgres --no-owner --role=keycloak --exit-on-error -d checked_backup_restore /tmp/checked-backup.dump && " +
            "psql -U postgres -d checked_backup_restore -Atc 'SELECT count(*) FROM client'");
        Assert.True(int.TryParse(clients.Trim(), out var count) && count >= 4);
        await fixture.ComposeAsync("cp", Path.Combine(backup, "web-auth.tar.gz"), "web:/tmp/checked-web-backup.tar.gz");
        await fixture.ComposeAsync("exec", "-T", "web", "bash", "-ec",
            "mkdir /tmp/checked-web-restore && tar -C /tmp/checked-web-restore -xzf /tmp/checked-web-backup.tar.gz && " +
            "test -n \"$(find /tmp/checked-web-restore/keys -name '*.xml' -print -quit)\"");
    }

    [Fact]
    public async Task Recovery_example_separates_publication_from_canonical_ports_and_uses_restored_secrets()
    {
        using var model = JsonDocument.Parse(await fixture.RenderRecoveryConfigurationAsync());
        var services = model.RootElement.GetProperty("services");
        var gateway = services.GetProperty("gateway");
        Assert.Equal("443", gateway.GetProperty("environment").GetProperty("HTTPS_PORT").GetString());
        foreach (var port in gateway.GetProperty("ports").EnumerateArray())
        {
            Assert.Equal("127.0.0.1", port.GetProperty("host_ip").GetString());
            var published = port.GetProperty("published").GetString();
            Assert.True((published == "18443" && port.GetProperty("target").GetInt32() == 443) ||
                        (published == "18080" && port.GetProperty("target").GetInt32() == 80));
        }
        var range = model.RootElement.GetProperty("networks").GetProperty("application").GetProperty("ipam").GetProperty("config")[0];
        Assert.Equal("172.30.60.0/24", range.GetProperty("subnet").GetString());
        Assert.Equal("172.30.60.128/25", range.GetProperty("ip_range").GetString());
        Assert.Equal("172.30.60.2", gateway.GetProperty("networks").GetProperty("application").GetProperty("ipv4_address").GetString());
        Assert.Equal("172.30.60.3", services.GetProperty("internal-proxy").GetProperty("networks").GetProperty("application").GetProperty("ipv4_address").GetString());
        foreach (var secret in model.RootElement.GetProperty("secrets").EnumerateObject())
            Assert.StartsWith("/srv/keycloak-starter/restored-secrets-check/", secret.Value.GetProperty("file").GetString());
    }

    [Fact]
    public async Task Keycloak_database_owner_has_only_the_required_role_attributes()
    {
        var attributes = await fixture.ComposeAsync("exec", "-T", "postgres", "psql", "--no-psqlrc", "--no-password",
            "--set=ON_ERROR_STOP=1", "--username", "postgres", "--dbname", "postgres", "-Atc",
            "SELECT r.rolcanlogin, r.rolsuper, r.rolcreatedb, r.rolcreaterole, r.rolinherit, r.rolreplication, r.rolbypassrls, " +
            "d.datdba = r.oid FROM pg_roles r JOIN pg_database d ON d.datname = 'keycloak' WHERE r.rolname = 'keycloak'");
        Assert.Equal("t|f|f|f|f|f|f|t", attributes.Trim());
    }

    [Fact]
    public async Task Postgres_backup_can_be_restored_into_an_isolated_database()
    {
        var result = await fixture.ComposeAsync("exec", "-T", "postgres", "sh", "-ec",
            "pg_dump -U postgres -Fc -f /tmp/verification.dump keycloak && " +
            "createdb -U postgres -O keycloak restore_verification && " +
            "pg_restore -U postgres --no-owner --role=keycloak --exit-on-error -d restore_verification /tmp/verification.dump && " +
            "psql -U postgres -d restore_verification -Atc 'SELECT count(*) FROM client'");
        Assert.True(int.TryParse(result.Trim(), out var clients) && clients >= 4);
    }
}
