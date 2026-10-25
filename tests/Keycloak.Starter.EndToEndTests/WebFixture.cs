using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Keycloak.Starter.EndToEndTests;

public class WebFixture : IAsyncLifetime
{
    protected readonly string _project = "starter-e2e-" + Guid.NewGuid().ToString("N")[..12];
    protected readonly string _temporary = Path.Combine(Path.GetTempPath(), "starter-e2e-" + Guid.NewGuid().ToString("N"));
    protected readonly Dictionary<string, string> _environment = new();
    protected string _root = "";
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _stackCreated;
    public string WebUrl { get; protected set; } = "";
    public string KeycloakUrl { get; protected set; } = "";

    public async ValueTask InitializeAsync()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("PLAYWRIGHT_INSTALL_BROWSERS") == "1")
            {
                if (Microsoft.Playwright.Program.Main(["install", "chromium"]) != 0)
                {
                    throw new InvalidOperationException("Playwright browser installation failed.");
                }
            }
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Keycloak.Starter.sln")))
            {
                directory = directory.Parent;
            }
            _root = directory?.FullName ?? throw new InvalidOperationException("Solution root not found.");
            Directory.CreateDirectory(_temporary);
            await PrepareStackAsync();
            _stackCreated = true;
            await ComposeAsync("up", "-d", "--build", "--wait", "--wait-timeout", "180");
        }
        catch (Exception exception)
        {
            var logs = "";
            if (_stackCreated)
            {
                try { logs = await ComposeAsync("logs", "--no-color", "--tail=20"); }
                catch (InvalidOperationException) { }
            }
            await DisposeAsync();
            throw new InvalidOperationException("Disposable stack startup failed. Container diagnostics:\n" + logs, exception);
        }
    }

    protected virtual string[] ComposeFiles => [Path.Combine(_root, "docker-compose.yml")];

    protected virtual async Task PrepareStackAsync()
    {
        using var webPort = new TcpListener(IPAddress.Loopback, 0);
        using var keycloakPort = new TcpListener(IPAddress.Loopback, 0);
        using var apiPort = new TcpListener(IPAddress.Loopback, 0);
        webPort.Start();
        keycloakPort.Start();
        apiPort.Start();
        using var transactionsPort = new TcpListener(IPAddress.Loopback, 0);
        transactionsPort.Start();
        _environment["TRANSACTIONS_PORT"] = ((IPEndPoint)transactionsPort.LocalEndpoint).Port.ToString();
        var web = ((IPEndPoint)webPort.LocalEndpoint).Port;
        var keycloak = ((IPEndPoint)keycloakPort.LocalEndpoint).Port;
        var api = ((IPEndPoint)apiPort.LocalEndpoint).Port;
        WebUrl = $"https://localhost:{web}";
        KeycloakUrl = $"http://localhost:{keycloak}";
        var realm = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "realm-export.json")))!;
        var client = realm["clients"]!.AsArray().First(value => value!["clientId"]!.GetValue<string>() == "starter-web")!;
        client["redirectUris"] = new JsonArray(WebUrl + "/signin-oidc");
        client["attributes"]!["post.logout.redirect.uris"] = WebUrl + "/signout-callback-oidc";
        client["attributes"]!["access.token.lifespan"] = "5";
        var realmPath = Path.Combine(_temporary, "realm-export.json");
        await File.WriteAllTextAsync(realmPath, realm.ToJsonString());
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        certificateRequest.CertificateExtensions.Add(names.Build());
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        await File.WriteAllBytesAsync(Path.Combine(_temporary, "web.pfx"),
            certificate.Export(X509ContentType.Pfx, "local-demo-certificate-password"));
        _environment["WEB_PORT"] = web.ToString();
        _environment["KEYCLOAK_PORT"] = keycloak.ToString();
        _environment["API_PORT"] = api.ToString();
        _environment["KEYCLOAK_REALM_FILE"] = realmPath;
        _environment["WEB_CERT_DIRECTORY"] = _temporary;
        _environment["WEB_CLIENT_SECRET"] = "local-demo-web-secret";
        _environment["HTTPS_CERT_PASSWORD"] = "local-demo-certificate-password";
        _environment["POSTGRES_ADMIN_USER"] = "postgres";
        _environment["POSTGRES_ADMIN_PASSWORD"] = "e2e-postgres-password";
        _environment["KEYCLOAK_DB_PASSWORD"] = "e2e-keycloak-password";
        _environment["KC_ADMIN_PASSWORD"] = "e2e-admin-password";
        webPort.Stop();
        keycloakPort.Stop();
        apiPort.Stop();
        transactionsPort.Stop();
    }

    public Task<string> WorkerLogsAsync() => ComposeAsync("logs", "--no-color", "worker");

    public Task<IBrowserContext> NewContextAsync() =>
        (_browser ?? throw new InvalidOperationException("Browser not started."))
        .NewContextAsync(new BrowserNewContextOptions
        {
            // Only this disposable localhost stack uses an untrusted test certificate.
            IgnoreHTTPSErrors = true
        });

    public async Task<string> RecreateWebAsync()
    {
        var before = (await ComposeAsync("ps", "-q", "web")).Trim();
        await ComposeAsync("up", "-d", "--no-deps", "--force-recreate", "--wait", "--wait-timeout", "120", "web");
        var after = (await ComposeAsync("ps", "-q", "web")).Trim();
        Assert.NotEqual(before, after);
        return after;
    }

    public async Task<string> ComposeAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        var prefix = new[] { "compose", "-p", _project }
            .Concat(ComposeFiles.SelectMany(file => new[] { "-f", file }));
        foreach (var argument in prefix.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var (key, value) in _environment)
        {
            start.Environment[key] = value;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Docker Compose.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        var stdout = await output;
        var stderr = await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Docker Compose failed: {stderr}\n{stdout}");
        }
        return stdout;
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }
        _playwright?.Dispose();
        _playwright = null;
        if (_stackCreated)
        {
            _stackCreated = false;
            await ComposeAsync("down", "--volumes", "--remove-orphans");
        }
        if (Directory.Exists(_temporary))
        {
            Directory.Delete(_temporary, recursive: true);
        }
    }
}
