using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Keycloak.Starter.Web.Security;

// Persistent encrypted sessions for this single-instance starter.
public sealed class FileTicketStore(string directory, IDataProtectionProvider provider) : ITicketStore, IDisposable
{
    public const string SessionKey = "starter_session_key";
    private readonly IDataProtector _protector = provider.CreateProtector("StarterWeb.AuthenticationTickets.v1");
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ticket.Properties.Items[SessionKey] = key;
        await SaveAsync(key, ticket, create: true);
        RemoveStaleFiles();
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => SaveAsync(key, ticket, create: false);

    private async Task SaveAsync(string key, AuthenticationTicket ticket, bool create)
    {
        await _writeLock.WaitAsync();
        try
        {
            var path = GetPath(key);
            // A refresh must never recreate a session that logout has removed.
            if (!create && !File.Exists(path))
            {
                return;
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var protectedTicket = _protector.Protect(TicketSerializer.Default.Serialize(ticket));
            try
            {
                await File.WriteAllBytesAsync(temporary, protectedTicket);
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var ticket = TicketSerializer.Default.Deserialize(_protector.Unprotect(bytes));
            if (ticket?.Properties.ExpiresUtc is not { } expiry || expiry <= DateTimeOffset.UtcNow)
            {
                File.Delete(path);
                return null;
            }
            return ticket;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task RemoveAsync(string key)
    {
        await _writeLock.WaitAsync();
        try
        {
            File.Delete(GetPath(key));
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private string GetPath(string key)
    {
        if (key.Length != 64 || !key.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("Invalid session key.", nameof(key));
        }
        return Path.Combine(directory, key + ".ticket");
    }

    private void RemoveStaleFiles()
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.ticket"))
        {
            if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-8))
            {
                File.Delete(file);
            }
        }
    }

    public void Dispose() => _writeLock.Dispose();
}
