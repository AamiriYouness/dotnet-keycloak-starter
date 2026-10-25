using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Keycloak.Starter.EndToEndTests;

public sealed class ProductionWebFixture : WebFixture
{
    private X509Certificate2? _ca;
    private int _httpsPort;
    private int _internalPort;
    public int HttpPort { get; private set; }
    public string WorkerSecret { get; private set; } = "";
    protected override string[] ComposeFiles =>
        [Path.Combine(_root, "deploy", "production", "compose.yml"), Path.Combine(_temporary, "test-compose.yml")];

    protected override async Task PrepareStackAsync()
    {
        using var https = new TcpListener(IPAddress.Loopback, 0);
        using var http = new TcpListener(IPAddress.Loopback, 0);
        using var internalProxy = new TcpListener(IPAddress.Loopback, 0);
        https.Start(); http.Start(); internalProxy.Start();
        _httpsPort = ((IPEndPoint)https.LocalEndpoint).Port;
        HttpPort = ((IPEndPoint)http.LocalEndpoint).Port;
        _internalPort = ((IPEndPoint)internalProxy.LocalEndpoint).Port;
        WebUrl = $"https://app.localhost:{_httpsPort}";
        KeycloakUrl = $"https://auth.localhost:{_httpsPort}";
        var secrets = Path.Combine(_temporary, "secrets");
        Directory.CreateDirectory(secrets);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(secrets, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var file in new[] { "postgres-admin-password", "keycloak-db-password", "keycloak-admin-password", "web-client-secret", "exchange-client-secret", "worker-client-secret" })
            await File.WriteAllTextAsync(Path.Combine(secrets, file), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        string Secret(string name) => File.ReadAllText(Path.Combine(secrets, name));
        WorkerSecret = Secret("worker-client-secret");
        await File.WriteAllTextAsync(Path.Combine(secrets, "api.json"), JsonSerializer.Serialize(new
            { TokenExchange = new { ClientSecret = Secret("exchange-client-secret") } }));
        await File.WriteAllTextAsync(Path.Combine(secrets, "worker.json"), JsonSerializer.Serialize(new
            { Worker = new { ClientSecret = WorkerSecret } }));
        var protectionPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await File.WriteAllTextAsync(Path.Combine(secrets, "web.json"), JsonSerializer.Serialize(new
            { Keycloak = new { ClientSecret = Secret("web-client-secret") }, DataProtection = new { CertificatePassword = protectionPassword } }));

        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=Starter disposable test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var authority = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(2));
        _ca = X509CertificateLoader.LoadCertificate(authority.Export(X509ContentType.Cert));
        await File.WriteAllTextAsync(Path.Combine(secrets, "ca-bundle.pem"), authority.ExportCertificatePem());
        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=app.localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        foreach (var name in new[] { "app.localhost", "auth.localhost", "accounts.localhost", "transactions.localhost" }) names.AddDnsName(name);
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        serverRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        using var signed = serverRequest.Create(authority, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
        await File.WriteAllTextAsync(Path.Combine(secrets, "tls-chain.pem"), signed.ExportCertificatePem());
        await File.WriteAllTextAsync(Path.Combine(secrets, "tls-key.pem"), serverKey.ExportPkcs8PrivateKeyPem());
        using var protectionKey = RSA.Create(2048);
        var protectionRequest = new CertificateRequest("CN=Test key protection", protectionKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var protection = protectionRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        await File.WriteAllBytesAsync(Path.Combine(secrets, "key-protection.pfx"), protection.Export(X509ContentType.Pfx, protectionPassword));
        foreach (var file in Directory.GetFiles(secrets))
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "deploy", "production", "starter-realm.json")))!;
        var development = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "keycloak", "realm-export.json")))!;
        foreach (var user in development["users"]!.AsArray().Where(user => user!["username"]!.GetValue<string>().StartsWith("demo-", StringComparison.Ordinal)))
            realm["users"]!.AsArray().Add(user!.DeepClone());
        var web = realm["clients"]!.AsArray().Single(client => client!["clientId"]!.GetValue<string>() == "starter-web")!;
        web["attributes"]!["access.token.lifespan"] = "5";
        var realmPath = Path.Combine(_temporary, "starter-realm.json");
        await File.WriteAllTextAsync(realmPath, realm.ToJsonString());
        var subnet = $"10.253.{RandomNumberGenerator.GetInt32(1, 250)}";
        _environment["WEB_HOST"] = "app.localhost";
        _environment["AUTH_HOST"] = "auth.localhost";
        _environment["ACCOUNTS_HOST"] = "accounts.localhost";
        _environment["TRANSACTIONS_HOST"] = "transactions.localhost";
        _environment["WEB_ORIGIN"] = WebUrl;
        _environment["KEYCLOAK_ORIGIN"] = KeycloakUrl;
        _environment["KEYCLOAK_ADMIN_USER"] = "production-test-admin";
        _environment["HTTPS_PORT"] = _httpsPort.ToString();
        _environment["HTTP_PORT"] = HttpPort.ToString();
        _environment["EDGE_BIND"] = "127.0.0.1";
        _environment["DEPLOYMENT_SECRETS_DIR"] = secrets;
        _environment["PRODUCTION_REALM_FILE"] = realmPath;
        _environment["APPLICATION_SUBNET"] = subnet + ".0/24";
        _environment["APPLICATION_DYNAMIC_RANGE"] = subnet + ".128/25";
        _environment["EDGE_PROXY_IP"] = subnet + ".2";
        _environment["INTERNAL_PROXY_IP"] = subnet + ".3";
        // Only this isolated test publishes the internal proxy so host-side security tests can reach it.
        await File.WriteAllTextAsync(Path.Combine(_temporary, "test-compose.yml"),
            $"services:\n  internal-proxy:\n    ports:\n      - '127.0.0.1:{_internalPort}:443'\n");
    }

    public HttpClient CreateHttpClient(bool internalProxy = false, bool redirect = false)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = redirect,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    var port = internalProxy ? _internalPort : context.DnsEndPoint.Port;
                    await socket.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                {
                    if (certificate is null || (errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) return false;
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(_ca!);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    return chain.Build((X509Certificate2)certificate);
                }
            }
        };
        return new HttpClient(handler);
    }
    private async Task<string> RunOperationAsync(ProcessStartInfo start)
    {
        start.WorkingDirectory = _root;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Operation did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(entireProcessTree: true); throw; }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException("Deployment operation failed: " + error + output);
        return output;
    }

    public async Task<string> CreateCheckedBackupAsync()
    {
        var environmentFile = Path.Combine(_temporary, "backup.env");
        await File.WriteAllTextAsync(environmentFile, "# Values are supplied by the isolated fixture environment.\n");
        var destination = Path.Combine(_temporary, "checked-backup");
        var start = new ProcessStartInfo("sh");
        foreach (var argument in new[] { Path.Combine(_root, "deploy", "production", "backup.sh"), environmentFile,
            _project, destination, Path.Combine(_temporary, "secrets") }) start.ArgumentList.Add(argument);
        foreach (var (key, value) in _environment) start.Environment[key] = value;
        start.Environment["STARTER_COMPOSE_OVERRIDE"] = Path.Combine(_temporary, "test-compose.yml");
        await RunOperationAsync(start);
        return destination;
    }

    public async Task<string> RenderRecoveryConfigurationAsync()
    {
        var start = new ProcessStartInfo("docker");
        foreach (var argument in new[] { "compose", "--env-file", Path.Combine(_root, "deploy", "production", "recovery.env.example"),
            "-f", Path.Combine(_root, "deploy", "production", "compose.yml"), "-p", "starter-recovery-check", "config", "--format", "json" })
            start.ArgumentList.Add(argument);
        foreach (var key in new[] { "WEB_HOST", "AUTH_HOST", "ACCOUNTS_HOST", "TRANSACTIONS_HOST", "WEB_ORIGIN", "KEYCLOAK_ORIGIN",
            "APPLICATION_SUBNET", "APPLICATION_DYNAMIC_RANGE", "EDGE_PROXY_IP", "INTERNAL_PROXY_IP", "EDGE_BIND", "HTTPS_PORT", "HTTP_PORT",
            "HTTPS_PUBLISHED_PORT", "HTTP_PUBLISHED_PORT", "KEYCLOAK_ADMIN_USER", "PRODUCTION_REALM_FILE" }) start.Environment.Remove(key);
        // Mirrors the documented explicit reset, overriding any inherited live path.
        start.Environment["DEPLOYMENT_SECRETS_DIR"] = "/srv/keycloak-starter/restored-secrets-check";
        return await RunOperationAsync(start);
    }

    public override async ValueTask DisposeAsync()
    {
        try { await base.DisposeAsync(); }
        finally { _ca?.Dispose(); }
    }
}
