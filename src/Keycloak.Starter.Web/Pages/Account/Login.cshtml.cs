using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Keycloak.Starter.Web.Pages.Account;

public sealed class LoginModel : PageModel
{
    public IActionResult OnGet(string? returnUrl)
    {
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = Url.IsLocalUrl(returnUrl) ? returnUrl : "/"
        }, OpenIdConnectDefaults.AuthenticationScheme);
    }
}
