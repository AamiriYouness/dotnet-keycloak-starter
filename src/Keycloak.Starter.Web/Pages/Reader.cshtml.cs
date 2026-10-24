using Keycloak.Starter.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Keycloak.Starter.Web.Pages;

public sealed class ReaderModel(StarterApiClient api) : PageModel
{
    public ApiResult Result { get; private set; } = new(0, "");

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Result = await api.GetAsync("reader", cancellationToken);
        if (Result.RequiresSignIn)
        {
            return Challenge();
        }
        Response.StatusCode = Result.StatusCode;
        return Page();
    }
}
