using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Keycloak.Starter.Web.Security;

public static class AuthenticationStorageExtensions
{
    public static IServiceCollection AddPersistentAuthenticationStorage(this IServiceCollection services)
    {
        services.AddSingleton<AuthenticationStorage>();
        services.AddDataProtection();
        services.AddSingleton<IDataProtectionProvider>(provider =>
        {
            var storage = provider.GetRequiredService<AuthenticationStorage>();
            return DataProtectionProvider.Create(storage.KeyDirectory, protection =>
            {
                protection.SetApplicationName("Keycloak.Starter.Web");
                if (storage.Certificate is not null)
                {
                    protection.ProtectKeysWithCertificate(storage.Certificate);
                }
            });
        });
        services.AddSingleton<FileTicketStore>(provider => new FileTicketStore(
            provider.GetRequiredService<AuthenticationStorage>().TicketDirectory.FullName,
            provider.GetRequiredService<IDataProtectionProvider>()));
        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<FileTicketStore>((options, store) => options.SessionStore = store);
        return services;
    }
}
