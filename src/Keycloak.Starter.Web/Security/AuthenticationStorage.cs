using System.Security.Cryptography.X509Certificates;
using Keycloak.Starter.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Keycloak.Starter.Web.Security;

public sealed class AuthenticationStorage : IDisposable
{
    public DirectoryInfo KeyDirectory { get; }
    public DirectoryInfo TicketDirectory { get; }
    public X509Certificate2? Certificate { get; }

    public AuthenticationStorage(
        IOptions<AuthenticationSettings> authentication,
        IOptions<DataProtectionSettings> protection)
    {
        var settings = authentication.Value;
        var keyProtection = protection.Value;
        KeyDirectory = PrepareDirectory(settings.KeyDirectory);
        TicketDirectory = PrepareDirectory(settings.TicketDirectory);
        if (!string.IsNullOrWhiteSpace(keyProtection.CertificatePath))
        {
            Certificate = X509CertificateLoader.LoadPkcs12FromFile(
                keyProtection.CertificatePath, keyProtection.CertificatePassword);
        }
    }

    private static DirectoryInfo PrepareDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return directory;
    }

    public void Dispose() => Certificate?.Dispose();
}
