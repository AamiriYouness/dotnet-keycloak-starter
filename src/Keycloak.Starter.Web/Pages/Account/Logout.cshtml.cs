using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Keycloak.Starter.Web.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public IActionResult OnPost()
    {
        return SignOut(new AuthenticationProperties { RedirectUri = "/SignedOut" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
