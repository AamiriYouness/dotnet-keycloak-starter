using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Keycloak.Starter.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.EndToEndTests;

public sealed class WebOptionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "starter-options-" + Guid.NewGuid().ToString("N"));
    private readonly string _certificate;

    public WebOptionsTests()
    {
        Directory.CreateDirectory(_root);
        _certificate = Path.Combine(_root, "key-protection.pfx");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=options-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
        File.WriteAllBytes(_certificate, certificate.Export(X509ContentType.Pfx, "options-test-password"));
    }

    private IHost CreateHost(string environment, Dictionary<string, string?>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            ContentRootPath = _root
        });
        var values = new Dictionary<string, string?>
        {
            ["Keycloak:Authority"] = "https://keycloak.example/realms/starter",
            ["Keycloak:ClientId"] = "starter-web",
            ["Keycloak:ClientSecret"] = "private-validation-test-secret",
            ["Api:BaseUrl"] = "https://api.example",
            ["DataProtection:CertificatePath"] = _certificate,
            ["DataProtection:CertificatePassword"] = "options-test-password"
        };
        foreach (var (key, value) in overrides ?? [])
        {
            values[key] = value;
        }
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddWebOptions(builder.Configuration, builder.Environment);
        return builder.Build();
    }

    [Theory]
    [InlineData("Keycloak:Authority", "", "Keycloak:Authority")]
    [InlineData("Keycloak:ClientId", "", "Keycloak:ClientId")]
    [InlineData("Keycloak:ClientSecret", " ", "Keycloak:ClientSecret")]
    [InlineData("Keycloak:Authority", "http://keycloak.example", "Keycloak:Authority")]
    [InlineData("Keycloak:MetadataAddress", "http://keycloak.example/discovery", "Keycloak:MetadataAddress")]
    [InlineData("Api:BaseUrl", "http://api.example", "Api:BaseUrl")]
    [InlineData("Authentication:RefreshBeforeExpirySeconds", "0", "Authentication:RefreshBeforeExpirySeconds")]
    [InlineData("Authentication:StoragePath", "relative-path", "Authentication:StoragePath")]
    [InlineData("DataProtection:CertificatePath", "", "DataProtection:CertificatePath")]
    [InlineData("DataProtection:CertificatePath", "/missing/starter-certificate.pfx", "DataProtection:CertificatePath")]
    public async Task Invalid_settings_fail_during_host_startup(string key, string value, string expected)
    {
        using var host = CreateHost("Production", new() { [key] = value });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(expected, exception.Message);
        Assert.DoesNotContain("private-validation-test-secret", exception.Message);
    }

    [Fact]
    public async Task Validation_reports_multiple_missing_keycloak_values_together()
    {
        using var host = CreateHost("Production", new()
        {
            ["Keycloak:ClientId"] = "",
            ["Keycloak:ClientSecret"] = ""
        });

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Keycloak:ClientId", exception.Message);
        Assert.Contains("Keycloak:ClientSecret", exception.Message);
    }

    [Fact]
    public async Task Development_accepts_local_http_and_uses_the_default_storage_directory()
    {
        using var host = CreateHost("Development", new()
        {
            ["Keycloak:Authority"] = "http://localhost:8080/realms/starter",
            ["Api:BaseUrl"] = "http://localhost:5150",
            ["DataProtection:CertificatePath"] = null
        });

        await host.StartAsync(TestContext.Current.CancellationToken);
        var settings = host.Services.GetRequiredService<IOptions<AuthenticationSettings>>().Value;

        Assert.Equal(Path.Combine(_root, ".local", "authentication"), settings.StoragePath);
        Assert.Equal(30, settings.RefreshBeforeExpirySeconds);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
